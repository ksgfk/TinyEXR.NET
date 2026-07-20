using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyEXR.V3.IO
{
    /// <summary>
    /// Incremental exact source backed only by bytes supplied so far. Missing data returns
    /// WouldBlock with the first exact gap; retrying after Supply is side-effect free.
    /// </summary>
    public sealed class SuppliedDataSource : IExactDataSource, IAsyncExactDataSource, IDisposable
    {
        public const int MaximumSegmentLength = 64 * 1024;

        public const long DefaultMaximumRetainedBytes = 64L * 1024L * 1024L;

        public const int DefaultMaximumSegmentCount = 4096;

        private readonly object _sync = new object();
        private List<Segment> _segments = new List<Segment>();
        private long? _knownLength;
        private long _storedByteCount;
        private bool _isComplete;
        private bool _disposed;

        public SuppliedDataSource(long length)
            : this(length, DefaultMaximumRetainedBytes, DefaultMaximumSegmentCount)
        {
        }

        public SuppliedDataSource(long length, long maximumRetainedBytes, int maximumSegmentCount)
            : this((long?)length, maximumRetainedBytes, maximumSegmentCount)
        {
        }

        private SuppliedDataSource(
            long? knownLength,
            long maximumRetainedBytes,
            int maximumSegmentCount)
        {
            if (knownLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(knownLength));
            }

            if (maximumRetainedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetainedBytes));
            }

            if (maximumSegmentCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSegmentCount));
            }

            _knownLength = knownLength;
            MaximumRetainedBytes = maximumRetainedBytes;
            MaximumSegmentCount = maximumSegmentCount;
        }

        /// <summary>
        /// Creates a streaming source whose final size is supplied later through Complete.
        /// A known empty source remains SuppliedDataSource(0).
        /// </summary>
        public static SuppliedDataSource CreateUnknownLength(
            long maximumRetainedBytes = DefaultMaximumRetainedBytes,
            int maximumSegmentCount = DefaultMaximumSegmentCount)
        {
            return new SuppliedDataSource(
                knownLength: null,
                maximumRetainedBytes,
                maximumSegmentCount);
        }

        public long MaximumRetainedBytes { get; }

        public int MaximumSegmentCount { get; }

        public bool HasKnownLength
        {
            get
            {
                lock (_sync)
                {
                    return _knownLength.HasValue;
                }
            }
        }

        public long Length
        {
            get
            {
                lock (_sync)
                {
                    return _knownLength ?? throw new InvalidOperationException(
                        "The supplied source length is unknown until Complete is called.");
                }
            }
        }

        public bool TryGetLength(out long length)
        {
            lock (_sync)
            {
                if (_knownLength.HasValue)
                {
                    length = _knownLength.Value;
                    return true;
                }

                length = 0;
                return false;
            }
        }

        public bool IsComplete
        {
            get
            {
                lock (_sync)
                {
                    return _isComplete;
                }
            }
        }

        /// <summary>
        /// Unique supplied bytes retained by the source, excluding object overhead.
        /// </summary>
        public long StoredByteCount
        {
            get
            {
                lock (_sync)
                {
                    return _storedByteCount;
                }
            }
        }

        public int SegmentCount
        {
            get
            {
                lock (_sync)
                {
                    return _segments.Count;
                }
            }
        }

        /// <summary>
        /// Copies newly supplied gaps into bounded owned segments. Identical overlap is
        /// idempotent; conflicts and budget failures reject the entire call before mutation.
        /// </summary>
        public void Supply(long offset, ReadOnlySpan<byte> data)
        {
            long end = DataRangeValidation.GetEnd(offset, data.Length);
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_isComplete)
                {
                    throw new InvalidOperationException("A completed supplied source cannot accept more data.");
                }

                if (_knownLength.HasValue && end > _knownLength.Value)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(data),
                        "The supplied range exceeds the declared source length.");
                }

                ValidateOverlaps(offset, end, data);
                if (data.Length == 0)
                {
                    return;
                }

                CalculateGapBudget(offset, end, out long uniqueByteCount, out int newSegmentCount);
                long retainedAfterSupply = checked(_storedByteCount + uniqueByteCount);
                if (retainedAfterSupply > MaximumRetainedBytes)
                {
                    throw new InvalidOperationException(
                        $"The supply would retain {retainedAfterSupply} bytes; the configured limit is {MaximumRetainedBytes}.");
                }

                int mergedSegmentCount = CalculateMergedSegmentCount(offset, end);
                if (mergedSegmentCount > MaximumSegmentCount)
                {
                    throw new InvalidOperationException(
                        $"The supply would retain {mergedSegmentCount} segments; the configured limit is {MaximumSegmentCount}.");
                }

                // Byte allocations begin only after conflict, retained-byte, and segment-count
                // preflight succeeds. Existing state is replaced only after all copies finish.
                List<Segment> additions = new List<Segment>(newSegmentCount);
                AddGaps(additions, offset, end, data);
                int segmentCountBeforeMerging = checked(_segments.Count + newSegmentCount);
                List<Segment> candidate = new List<Segment>(segmentCountBeforeMerging);
                candidate.AddRange(_segments);
                candidate.AddRange(additions);
                candidate.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
                List<Segment> merged = MergeAdjacent(candidate);
                if (merged.Count != mergedSegmentCount)
                {
                    throw new InvalidOperationException("The segment budget preflight did not match the supplied layout.");
                }

                _segments = merged;
                _storedByteCount = retainedAfterSupply;
            }
        }

        /// <summary>
        /// Declares that no more bytes will be supplied. Repeating the same final length is
        /// idempotent; a different length or a length below supplied data is rejected.
        /// </summary>
        public void Complete(long finalLength)
        {
            if (finalLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(finalLength));
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                if (_isComplete)
                {
                    if (_knownLength == finalLength)
                    {
                        return;
                    }

                    throw new InvalidOperationException("The supplied source is already complete with a different length.");
                }

                if (_knownLength.HasValue && _knownLength.Value != finalLength)
                {
                    throw new InvalidOperationException(
                        $"The known source length is {_knownLength.Value}, not {finalLength}.");
                }

                if (_segments.Count != 0 && _segments[_segments.Count - 1].End > finalLength)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(finalLength),
                        "The final length is below bytes that have already been supplied.");
                }

                _knownLength = finalLength;
                _isComplete = true;
            }
        }

        public DataTransferResult ReadExactly(long offset, Span<byte> destination)
        {
            long end = DataRangeValidation.GetEnd(offset, destination.Length);
            lock (_sync)
            {
                if (_disposed)
                {
                    return DataTransferResult.Disposed(0, new ObjectDisposedException(nameof(SuppliedDataSource)));
                }

                if (_knownLength.HasValue && end > _knownLength.Value)
                {
                    return DataTransferResult.EndOfSource(0);
                }

                DataRange? pendingRange = FindPendingRange(offset, end);
                if (pendingRange.HasValue)
                {
                    return _isComplete
                        ? DataTransferResult.EndOfSource(0)
                        : DataTransferResult.WouldBlock(pendingRange.Value);
                }

                CopyRange(offset, end, destination);
                return DataTransferResult.Success(destination.Length);
            }
        }

        public ValueTask<DataTransferResult> ReadExactlyAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            DataRangeValidation.GetEnd(offset, destination.Length);
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<DataTransferResult>(DataTransferResult.Canceled(
                    0,
                    new OperationCanceledException(cancellationToken)));
            }

            return new ValueTask<DataTransferResult>(ReadExactly(offset, destination.Span));
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _segments.Clear();
                _storedByteCount = 0;
            }
        }

        private void ValidateOverlaps(long offset, long end, ReadOnlySpan<byte> data)
        {
            int index = FindFirstSegmentWhoseEndExceeds(offset);
            for (; index < _segments.Count; index++)
            {
                Segment segment = _segments[index];
                if (segment.Offset >= end)
                {
                    break;
                }

                long overlapStart = Math.Max(offset, segment.Offset);
                long overlapEnd = Math.Min(end, segment.End);
                int suppliedIndex = checked((int)(overlapStart - offset));
                int segmentIndex = checked((int)(overlapStart - segment.Offset));
                int overlapLength = checked((int)(overlapEnd - overlapStart));
                if (!data.Slice(suppliedIndex, overlapLength).SequenceEqual(
                    segment.Data.AsSpan(segmentIndex, overlapLength)))
                {
                    throw new InvalidOperationException(
                        $"Supplied bytes conflict with the existing segment at offset {overlapStart}.");
                }
            }
        }

        private void CalculateGapBudget(
            long offset,
            long end,
            out long uniqueByteCount,
            out int newSegmentCount)
        {
            uniqueByteCount = 0;
            newSegmentCount = 0;
            long cursor = offset;
            int index = FindFirstSegmentWhoseEndExceeds(cursor);
            for (; index < _segments.Count; index++)
            {
                Segment segment = _segments[index];
                if (segment.Offset >= end)
                {
                    break;
                }

                if (segment.Offset > cursor)
                {
                    AddGapBudget(cursor, Math.Min(segment.Offset, end), ref uniqueByteCount, ref newSegmentCount);
                }

                cursor = Math.Max(cursor, segment.End);
                if (cursor >= end)
                {
                    return;
                }
            }

            if (cursor < end)
            {
                AddGapBudget(cursor, end, ref uniqueByteCount, ref newSegmentCount);
            }
        }

        private static void AddGapBudget(
            long gapStart,
            long gapEnd,
            ref long uniqueByteCount,
            ref int newSegmentCount)
        {
            long gapLength = gapEnd - gapStart;
            uniqueByteCount = checked(uniqueByteCount + gapLength);
            long chunks = (gapLength + MaximumSegmentLength - 1L) / MaximumSegmentLength;
            newSegmentCount = checked(newSegmentCount + checked((int)chunks));
        }

        private int CalculateMergedSegmentCount(long offset, long end)
        {
            bool hasCurrent = false;
            long currentEnd = 0;
            int currentLength = 0;
            int completedCount = 0;
            long supplyCursor = offset;

            foreach (Segment segment in _segments)
            {
                if (segment.End <= offset)
                {
                    SimulateSegment(
                        segment.Offset,
                        segment.Data.Length,
                        ref hasCurrent,
                        ref currentEnd,
                        ref currentLength,
                        ref completedCount);
                    continue;
                }

                if (segment.Offset >= end)
                {
                    if (supplyCursor < end)
                    {
                        SimulateGap(
                            supplyCursor,
                            end,
                            ref hasCurrent,
                            ref currentEnd,
                            ref currentLength,
                            ref completedCount);
                        supplyCursor = end;
                    }

                    SimulateSegment(
                        segment.Offset,
                        segment.Data.Length,
                        ref hasCurrent,
                        ref currentEnd,
                        ref currentLength,
                        ref completedCount);
                    continue;
                }

                if (segment.Offset > supplyCursor)
                {
                    SimulateGap(
                        supplyCursor,
                        Math.Min(segment.Offset, end),
                        ref hasCurrent,
                        ref currentEnd,
                        ref currentLength,
                        ref completedCount);
                }

                SimulateSegment(
                    segment.Offset,
                    segment.Data.Length,
                    ref hasCurrent,
                    ref currentEnd,
                    ref currentLength,
                    ref completedCount);
                supplyCursor = Math.Max(supplyCursor, segment.End);
            }

            if (supplyCursor < end)
            {
                SimulateGap(
                    supplyCursor,
                    end,
                    ref hasCurrent,
                    ref currentEnd,
                    ref currentLength,
                    ref completedCount);
            }

            return checked(completedCount + (hasCurrent ? 1 : 0));
        }

        private static void SimulateGap(
            long gapStart,
            long gapEnd,
            ref bool hasCurrent,
            ref long currentEnd,
            ref int currentLength,
            ref int completedCount)
        {
            long cursor = gapStart;
            while (cursor < gapEnd)
            {
                int length = checked((int)Math.Min(MaximumSegmentLength, gapEnd - cursor));
                SimulateSegment(
                    cursor,
                    length,
                    ref hasCurrent,
                    ref currentEnd,
                    ref currentLength,
                    ref completedCount);
                cursor = checked(cursor + length);
            }
        }

        private static void SimulateSegment(
            long offset,
            int length,
            ref bool hasCurrent,
            ref long currentEnd,
            ref int currentLength,
            ref int completedCount)
        {
            if (hasCurrent && currentEnd == offset && currentLength + length <= MaximumSegmentLength)
            {
                currentLength = checked(currentLength + length);
                currentEnd = checked(currentEnd + length);
                return;
            }

            if (hasCurrent)
            {
                completedCount = checked(completedCount + 1);
            }

            hasCurrent = true;
            currentEnd = checked(offset + length);
            currentLength = length;
        }

        private void AddGaps(
            List<Segment> additions,
            long supplyOffset,
            long end,
            ReadOnlySpan<byte> data)
        {
            long cursor = supplyOffset;
            int index = FindFirstSegmentWhoseEndExceeds(cursor);
            for (; index < _segments.Count; index++)
            {
                Segment segment = _segments[index];
                if (segment.Offset >= end)
                {
                    break;
                }

                if (segment.Offset > cursor)
                {
                    AddGap(additions, supplyOffset, cursor, Math.Min(segment.Offset, end), data);
                }

                cursor = Math.Max(cursor, segment.End);
                if (cursor >= end)
                {
                    return;
                }
            }

            if (cursor < end)
            {
                AddGap(additions, supplyOffset, cursor, end, data);
            }
        }

        private static void AddGap(
            List<Segment> additions,
            long supplyOffset,
            long gapStart,
            long gapEnd,
            ReadOnlySpan<byte> data)
        {
            long cursor = gapStart;
            while (cursor < gapEnd)
            {
                int chunkLength = checked((int)Math.Min(MaximumSegmentLength, gapEnd - cursor));
                int sourceIndex = checked((int)(cursor - supplyOffset));
                additions.Add(new Segment(cursor, data.Slice(sourceIndex, chunkLength).ToArray()));
                cursor = checked(cursor + chunkLength);
            }
        }

        private static List<Segment> MergeAdjacent(List<Segment> segments)
        {
            if (segments.Count < 2)
            {
                return segments;
            }

            List<Segment> merged = new List<Segment>(segments.Count);
            Segment current = segments[0];
            for (int i = 1; i < segments.Count; i++)
            {
                Segment next = segments[i];
                int combinedLength = current.End == next.Offset
                    ? checked(current.Data.Length + next.Data.Length)
                    : MaximumSegmentLength + 1;
                if (combinedLength <= MaximumSegmentLength)
                {
                    byte[] combined = new byte[combinedLength];
                    Buffer.BlockCopy(current.Data, 0, combined, 0, current.Data.Length);
                    Buffer.BlockCopy(next.Data, 0, combined, current.Data.Length, next.Data.Length);
                    current = new Segment(current.Offset, combined);
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }

        private DataRange? FindPendingRange(long offset, long end)
        {
            if (offset == end)
            {
                return null;
            }

            long cursor = offset;
            int index = FindFirstSegmentWhoseEndExceeds(cursor);
            for (; index < _segments.Count; index++)
            {
                Segment segment = _segments[index];
                if (segment.Offset > cursor)
                {
                    long pendingEnd = Math.Min(segment.Offset, end);
                    return new DataRange(cursor, pendingEnd - cursor);
                }

                cursor = Math.Min(end, segment.End);
                if (cursor == end)
                {
                    return null;
                }
            }

            return cursor == end ? null : new DataRange(cursor, end - cursor);
        }

        private void CopyRange(long offset, long end, Span<byte> destination)
        {
            long cursor = offset;
            int destinationIndex = 0;
            int index = FindFirstSegmentWhoseEndExceeds(cursor);
            for (; index < _segments.Count; index++)
            {
                Segment segment = _segments[index];
                if (segment.Offset > cursor || cursor == end)
                {
                    break;
                }

                long copyEnd = Math.Min(segment.End, end);
                int segmentIndex = checked((int)(cursor - segment.Offset));
                int copyLength = checked((int)(copyEnd - cursor));
                segment.Data.AsSpan(segmentIndex, copyLength).CopyTo(
                    destination.Slice(destinationIndex, copyLength));
                destinationIndex = checked(destinationIndex + copyLength);
                cursor = copyEnd;
            }

            if (cursor != end || destinationIndex != destination.Length)
            {
                throw new InvalidOperationException("The supplied range changed while it was being copied.");
            }
        }

        private int FindFirstSegmentWhoseEndExceeds(long offset)
        {
            int low = 0;
            int high = _segments.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (_segments[middle].End <= offset)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SuppliedDataSource));
            }
        }

        private sealed class Segment
        {
            public Segment(long offset, byte[] data)
            {
                Offset = offset;
                Data = data;
                End = checked(offset + data.Length);
            }

            public long Offset { get; }

            public long End { get; }

            public byte[] Data { get; }
        }
    }
}
