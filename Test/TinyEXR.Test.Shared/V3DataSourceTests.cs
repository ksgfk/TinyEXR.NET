using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3DataSourceTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 memory source is exact and zero-copy backed")]
    public void Case_V3DataSource_MemorySourceIsExactAndZeroCopyBacked()
    {
        byte[] data = Enumerable.Range(0, 10).Select(static value => (byte)value).ToArray();
        V3IO.MemoryDataSource source = new(data);
        byte[] destination = new byte[4];

        V3IO.DataTransferResult result = source.ReadExactly(3, destination);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, result.Status);
        Assert.AreEqual(4L, result.BytesTransferred);
        CollectionAssert.AreEqual(new byte[] { 3, 4, 5, 6 }, destination);

        Assert.IsTrue(source.TryGetMemory(2, 3, out ReadOnlyMemory<byte> slice));
        data[2] = 99;
        Assert.AreEqual((byte)99, slice.Span[0]);

        byte[] untouched = Enumerable.Repeat((byte)0xcc, 4).ToArray();
        V3IO.DataTransferResult eof = source.ReadExactly(8, untouched);
        Assert.AreEqual(V3IO.DataTransferStatus.EndOfSource, eof.Status);
        Assert.AreEqual(0L, eof.BytesTransferred);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xcc, 4).ToArray(), untouched);
        Assert.IsFalse(source.TryGetMemory(9, 2, out _));

        AssertThrows<ArgumentOutOfRangeException>(() => source.ReadExactly(-1, new byte[1]));
        AssertThrows<OverflowException>(() => source.ReadExactly(long.MaxValue, new byte[1]));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 stream source maps short reads EOF and disposal")]
    public void Case_V3DataSource_StreamSourceMapsShortReadsEofAndDisposal()
    {
        byte[] data = Enumerable.Range(0, 10).Select(static value => (byte)value).ToArray();
        using ChunkedMemoryStream stream = new(data, maximumRead: 2);
        V3IO.StreamDataSource source = new(stream, leaveOpen: true);

        byte[] destination = new byte[5];
        V3IO.DataTransferResult success = source.ReadExactly(1, destination);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, success.Status);
        Assert.AreEqual(5L, success.BytesTransferred);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, destination);
        Assert.AreEqual(6L, stream.Position);
        Assert.IsTrue(stream.ReadCallCount >= 3);

        byte[] partial = Enumerable.Repeat((byte)0xee, 5).ToArray();
        V3IO.DataTransferResult eof = source.ReadExactly(8, partial);
        Assert.AreEqual(V3IO.DataTransferStatus.EndOfSource, eof.Status);
        Assert.AreEqual(2L, eof.BytesTransferred);
        Assert.AreEqual((byte)8, partial[0]);
        Assert.AreEqual((byte)9, partial[1]);
        Assert.AreEqual((byte)0xee, partial[2]);

        source.Dispose();
        V3IO.DataTransferResult disposed = source.ReadExactly(0, new byte[1]);
        Assert.AreEqual(V3IO.DataTransferStatus.Disposed, disposed.Status);
        Assert.IsTrue(disposed.Error is ObjectDisposedException);
        Assert.IsTrue(stream.CanRead);

        using NonSeekableReadStream nonSeekable = new();
        AssertThrows<ArgumentException>(() => new V3IO.StreamDataSource(nonSeekable));

        MemoryStream externallyDisposed = new(data, writable: false);
        V3IO.StreamDataSource externalSource = new(externallyDisposed, leaveOpen: true);
        externallyDisposed.Dispose();
        V3IO.DataTransferResult externalResult = externalSource.ReadExactly(0, new byte[1]);
        Assert.AreEqual(V3IO.DataTransferStatus.Disposed, externalResult.Status);

        using ThrowingReadStream throwing = new(data);
        using V3IO.StreamDataSource throwingSource = new(throwing, leaveOpen: true);
        V3IO.DataTransferResult ioError = throwingSource.ReadExactly(0, new byte[1]);
        Assert.AreEqual(V3IO.DataTransferStatus.IoError, ioError.Status);
        Assert.IsTrue(ioError.Error is IOException);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 supplied source reports exact gaps and retries atomically")]
    public void Case_V3DataSource_SuppliedSourceReportsExactGapsAndRetriesAtomically()
    {
        using V3IO.SuppliedDataSource source = new(1_000_000_000L);
        byte[] expected = Enumerable.Range(0, 10).Select(static value => (byte)value).ToArray();
        source.Supply(10, expected.AsSpan(0, 4));
        source.Supply(17, expected.AsSpan(7, 3));

        Assert.AreEqual(
            V3IO.DataTransferStatus.Success,
            source.ReadExactly(15, Span<byte>.Empty).Status);

        byte[] destination = Enumerable.Repeat((byte)0xcc, 10).ToArray();
        V3IO.DataTransferResult blocked = source.ReadExactly(10, destination);
        Assert.AreEqual(V3IO.DataTransferStatus.WouldBlock, blocked.Status);
        Assert.AreEqual(0L, blocked.BytesTransferred);
        Assert.IsTrue(blocked.PendingRange.HasValue);
        Assert.AreEqual(14L, blocked.PendingRange.Value.Offset);
        Assert.AreEqual(3L, blocked.PendingRange.Value.Length);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xcc, 10).ToArray(), destination);

        source.Supply(14, expected.AsSpan(4, 3));
        V3IO.DataTransferResult success = source.ReadExactly(10, destination);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, success.Status);
        CollectionAssert.AreEqual(expected, destination);
        Assert.AreEqual(10L, source.StoredByteCount);

        source.Supply(12, expected.AsSpan(2, 6));
        Assert.AreEqual(10L, source.StoredByteCount);
        long beforeConflict = source.StoredByteCount;
        AssertThrows<InvalidOperationException>(() => source.Supply(15, new byte[] { 0xff }));
        Assert.AreEqual(beforeConflict, source.StoredByteCount);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, source.ReadExactly(10, destination).Status);
        CollectionAssert.AreEqual(expected, destination);

        source.Supply(8, new byte[] { 100, 101, 0, 1 });
        Assert.AreEqual(12L, source.StoredByteCount);
        byte[] extended = new byte[12];
        Assert.AreEqual(V3IO.DataTransferStatus.Success, source.ReadExactly(8, extended).Status);
        CollectionAssert.AreEqual(
            new byte[] { 100, 101, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            extended);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 supplied source allocates only bounded supplied segments")]
    public void Case_V3DataSource_SuppliedSourceAllocatesOnlyBoundedSuppliedSegments()
    {
        using V3IO.SuppliedDataSource source = new(long.MaxValue);
        Assert.AreEqual(0L, source.StoredByteCount);
        Assert.AreEqual(0, source.SegmentCount);

        byte[] supplied = Enumerable.Range(0, (V3IO.SuppliedDataSource.MaximumSegmentLength * 2) + 1)
            .Select(static value => (byte)value)
            .ToArray();
        const long offset = 4_000_000_000L;
        source.Supply(offset, supplied);

        Assert.AreEqual((long)supplied.Length, source.StoredByteCount);
        Assert.AreEqual(3, source.SegmentCount);
        byte[] acrossBoundary = new byte[4];
        long boundaryOffset = offset + V3IO.SuppliedDataSource.MaximumSegmentLength - 2L;
        Assert.AreEqual(
            V3IO.DataTransferStatus.Success,
            source.ReadExactly(boundaryOffset, acrossBoundary).Status);
        CollectionAssert.AreEqual(
            supplied.AsSpan(V3IO.SuppliedDataSource.MaximumSegmentLength - 2, 4).ToArray(),
            acrossBoundary);

        source.Dispose();
        V3IO.DataTransferResult disposed = source.ReadExactly(offset, new byte[1]);
        Assert.AreEqual(V3IO.DataTransferStatus.Disposed, disposed.Status);
        Assert.AreEqual(0L, source.StoredByteCount);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 supplied source budgets reject fragmentation transactionally")]
    public void Case_V3DataSource_SuppliedSourceBudgetsRejectFragmentationTransactionally()
    {
        using V3IO.SuppliedDataSource fragmented = new(
            length: 1000,
            maximumRetainedBytes: 128,
            maximumSegmentCount: 64);
        for (int i = 0; i < 64; i++)
        {
            fragmented.Supply(i * 2L, new byte[] { (byte)(10 + i) });
        }

        Assert.AreEqual(64L, fragmented.StoredByteCount);
        Assert.AreEqual(64, fragmented.SegmentCount);
        AssertThrows<InvalidOperationException>(() => fragmented.Supply(128, new byte[] { 74 }));
        Assert.AreEqual(64L, fragmented.StoredByteCount);
        Assert.AreEqual(64, fragmented.SegmentCount);
        byte[] retained = new byte[1];
        Assert.AreEqual(V3IO.DataTransferStatus.Success, fragmented.ReadExactly(4, retained).Status);
        Assert.AreEqual((byte)12, retained[0]);

        using V3IO.SuppliedDataSource adjacent = new(
            length: 10,
            maximumRetainedBytes: 10,
            maximumSegmentCount: 1);
        adjacent.Supply(0, new byte[] { 1 });
        adjacent.Supply(1, new byte[] { 2 });
        Assert.AreEqual(2L, adjacent.StoredByteCount);
        Assert.AreEqual(1, adjacent.SegmentCount);

        using V3IO.SuppliedDataSource byteLimited = new(
            length: 10,
            maximumRetainedBytes: 2,
            maximumSegmentCount: 10);
        byteLimited.Supply(0, new byte[] { 1, 2 });
        byteLimited.Supply(0, new byte[] { 1, 2 });
        AssertThrows<InvalidOperationException>(() => byteLimited.Supply(2, new byte[] { 3 }));
        Assert.AreEqual(2L, byteLimited.StoredByteCount);
        Assert.AreEqual(1, byteLimited.SegmentCount);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 unknown supplied length becomes final only at Complete")]
    public void Case_V3DataSource_UnknownSuppliedLengthBecomesFinalOnlyAtComplete()
    {
        using V3IO.SuppliedDataSource knownEmpty = new(0);
        Assert.IsTrue(knownEmpty.HasKnownLength);
        Assert.AreEqual(0L, knownEmpty.Length);

        using V3IO.SuppliedDataSource source = V3IO.SuppliedDataSource.CreateUnknownLength(
            maximumRetainedBytes: 32,
            maximumSegmentCount: 8);
        Assert.IsFalse(source.HasKnownLength);
        Assert.IsFalse(source.TryGetLength(out _));
        AssertThrows<InvalidOperationException>(() => _ = source.Length);

        source.Supply(5, new byte[] { 50, 51 });
        byte[] available = new byte[2];
        Assert.AreEqual(V3IO.DataTransferStatus.Success, source.ReadExactly(5, available).Status);
        CollectionAssert.AreEqual(new byte[] { 50, 51 }, available);
        V3IO.DataTransferResult pending = source.ReadExactly(0, new byte[1]);
        Assert.AreEqual(V3IO.DataTransferStatus.WouldBlock, pending.Status);
        Assert.AreEqual(0L, pending.PendingRange!.Value.Offset);
        Assert.AreEqual(1L, pending.PendingRange.Value.Length);
        AssertThrows<OverflowException>(() => source.Supply(long.MaxValue, new byte[1]));

        source.Complete(7);
        Assert.IsTrue(source.IsComplete);
        Assert.IsTrue(source.HasKnownLength);
        Assert.IsTrue(source.TryGetLength(out long finalLength));
        Assert.AreEqual(7L, finalLength);
        Assert.AreEqual(V3IO.DataTransferStatus.EndOfSource, source.ReadExactly(0, new byte[1]).Status);
        Assert.AreEqual(V3IO.DataTransferStatus.EndOfSource, source.ReadExactly(6, new byte[2]).Status);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, source.ReadExactly(7, Span<byte>.Empty).Status);
        source.Complete(7);
        AssertThrows<InvalidOperationException>(() => source.Complete(8));
        AssertThrows<InvalidOperationException>(() => source.Supply(5, new byte[] { 50 }));

        using V3IO.SuppliedDataSource rejected = V3IO.SuppliedDataSource.CreateUnknownLength();
        rejected.Supply(10, new byte[] { 1, 2 });
        AssertThrows<ArgumentOutOfRangeException>(() => rejected.Complete(11));
        Assert.IsFalse(rejected.HasKnownLength);
        rejected.Complete(12);
        Assert.AreEqual(12L, rejected.Length);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 async sources cancel and use genuinely asynchronous short reads")]
    public async Task Case_V3DataSource_AsyncSourcesCancelAndUseGenuinelyAsynchronousShortReads()
    {
        byte[] data = Enumerable.Range(0, 7).Select(static value => (byte)value).ToArray();
        await using AsyncChunkedMemoryStream stream = new(data, maximumRead: 2);
        await using V3IO.StreamDataSource source = new(stream, leaveOpen: true);

        byte[] destination = new byte[7];
        ValueTask<V3IO.DataTransferResult> operation = source.ReadExactlyAsync(0, destination);
        Assert.IsFalse(operation.IsCompleted);
        stream.ReleaseReads();
        V3IO.DataTransferResult success = await operation;
        Assert.AreEqual(V3IO.DataTransferStatus.Success, success.Status);
        CollectionAssert.AreEqual(data, destination);
        Assert.AreEqual(0, stream.SyncReadCallCount);
        Assert.IsTrue(stream.AsyncReadCallCount >= 4);

        byte[] partial = new byte[4];
        V3IO.DataTransferResult eof = await source.ReadExactlyAsync(5, partial);
        Assert.AreEqual(V3IO.DataTransferStatus.EndOfSource, eof.Status);
        Assert.AreEqual(2L, eof.BytesTransferred);

        await using AsyncChunkedMemoryStream cancelStream = new(data, maximumRead: 2);
        await using V3IO.StreamDataSource cancelSource = new(cancelStream, leaveOpen: true);
        using CancellationTokenSource cancellation = new();
        ValueTask<V3IO.DataTransferResult> pendingCancellation = cancelSource.ReadExactlyAsync(
            0,
            new byte[1],
            cancellation.Token);
        Assert.IsFalse(pendingCancellation.IsCompleted);
        cancellation.Cancel();
        V3IO.DataTransferResult midReadCanceled = await pendingCancellation;
        Assert.AreEqual(V3IO.DataTransferStatus.Canceled, midReadCanceled.Status);
        Assert.IsTrue(midReadCanceled.Error is OperationCanceledException);

        using CancellationTokenSource preCancellation = new();
        preCancellation.Cancel();
        V3IO.DataTransferResult canceled = await source.ReadExactlyAsync(
            0,
            new byte[1],
            preCancellation.Token);
        Assert.AreEqual(V3IO.DataTransferStatus.Canceled, canceled.Status);
        Assert.IsTrue(canceled.Error is OperationCanceledException);

        V3IO.MemoryDataSource memory = new(data);
        V3IO.DataTransferResult memoryCanceled = await memory.ReadExactlyAsync(
            0,
            new byte[1],
            preCancellation.Token);
        Assert.AreEqual(V3IO.DataTransferStatus.Canceled, memoryCanceled.Status);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 async sources validate ranges before pre-cancellation")]
    public async Task Case_V3DataSource_AsyncSourcesValidateRangesBeforePreCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        V3IO.MemoryDataSource memory = new(new byte[4]);
        using V3IO.SuppliedDataSource supplied = V3IO.SuppliedDataSource.CreateUnknownLength();
        using MemoryStream stream = new(new byte[4], writable: false);
        await using V3IO.StreamDataSource streamSource = new(stream, leaveOpen: true);

        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await memory.ReadExactlyAsync(-1, new byte[1], cancellation.Token));
        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await supplied.ReadExactlyAsync(-1, new byte[1], cancellation.Token));
        await AssertThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await streamSource.ReadExactlyAsync(-1, new byte[1], cancellation.Token));
        await AssertThrowsAsync<OverflowException>(async () =>
            await memory.ReadExactlyAsync(long.MaxValue, new byte[1], cancellation.Token));
        await AssertThrowsAsync<OverflowException>(async () =>
            await supplied.ReadExactlyAsync(long.MaxValue, new byte[1], cancellation.Token));
        await AssertThrowsAsync<OverflowException>(async () =>
            await streamSource.ReadExactlyAsync(long.MaxValue, new byte[1], cancellation.Token));

        Assert.AreEqual(
            V3IO.DataTransferStatus.Canceled,
            (await memory.ReadExactlyAsync(0, new byte[1], cancellation.Token)).Status);
        Assert.AreEqual(
            V3IO.DataTransferStatus.Canceled,
            (await supplied.ReadExactlyAsync(0, new byte[1], cancellation.Token)).Status);
        Assert.AreEqual(
            V3IO.DataTransferStatus.Canceled,
            (await streamSource.ReadExactlyAsync(0, new byte[1], cancellation.Token)).Status);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 stream sink seeks backpatches and honors leaveOpen")]
    public void Case_V3DataSource_StreamSinkSeeksBackpatchesAndHonorsLeaveOpen()
    {
        MemoryStream stream = new();
        V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, sink.Write(new byte[4]).Status);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, sink.Write(new byte[] { 10, 11, 12 }).Status);
        Assert.AreEqual(7L, sink.Position);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, sink.Seek(0).Status);
        Assert.AreEqual(
            V3IO.DataTransferStatus.Success,
            sink.Write(new byte[] { 3, 0, 0, 0 }).Status);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, sink.Flush().Status);
        sink.Dispose();

        CollectionAssert.AreEqual(new byte[] { 3, 0, 0, 0, 10, 11, 12 }, stream.ToArray());
        stream.Position = stream.Length;
        stream.WriteByte(13);
        Assert.AreEqual(8L, stream.Length);
        Assert.AreEqual(V3IO.DataTransferStatus.Disposed, sink.Write(new byte[1]).Status);
        stream.Dispose();

        MemoryStream owned = new();
        V3IO.StreamDataSink ownedSink = new(owned);
        ownedSink.Write(new byte[] { 1 });
        ownedSink.Dispose();
        AssertThrows<ObjectDisposedException>(() => owned.WriteByte(2));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 async stream sink never falls back to synchronous writes")]
    public async Task Case_V3DataSource_AsyncStreamSinkNeverFallsBackToSynchronousWrites()
    {
        AsyncOnlyWriteMemoryStream stream = new();
        V3IO.StreamDataSink sink = new(stream, leaveOpen: true);

        ValueTask<V3IO.DataTransferResult> write = sink.WriteAsync(new byte[] { 0, 0, 0, 0, 8, 9 });
        Assert.IsFalse(write.IsCompleted);
        stream.ReleaseWrites();
        Assert.AreEqual(V3IO.DataTransferStatus.Success, (await write).Status);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, (await sink.SeekAsync(0)).Status);
        Assert.AreEqual(
            V3IO.DataTransferStatus.Success,
            (await sink.WriteAsync(new byte[] { 2, 0, 0, 0 })).Status);
        Assert.AreEqual(V3IO.DataTransferStatus.Success, (await sink.FlushAsync()).Status);
        Assert.AreEqual(0, stream.SyncWriteCallCount);
        Assert.IsTrue(stream.AsyncWriteCallCount >= 2);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        V3IO.DataTransferResult canceled = await sink.WriteAsync(new byte[] { 1 }, cancellation.Token);
        Assert.AreEqual(V3IO.DataTransferStatus.Canceled, canceled.Status);

        await sink.DisposeAsync();
        Assert.IsFalse(stream.IsDisposed);
        CollectionAssert.AreEqual(new byte[] { 2, 0, 0, 0, 8, 9 }, stream.ToArray());
        await stream.DisposeAsync();

        AsyncOnlyWriteMemoryStream owned = new();
        V3IO.StreamDataSink ownedSink = new(owned);
        await ownedSink.DisposeAsync();
        Assert.IsTrue(owned.IsDisposed);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 stream sink reports synchronous partial writes and remains reusable")]
    public void Case_V3DataSource_StreamSinkReportsSynchronousPartialWritesAndRemainsReusable()
    {
        using PartialFailingWriteMemoryStream stream = new(prefixLength: 2);
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        byte[] data = new byte[] { 1, 2, 3, 4 };

        V3IO.DataTransferResult failed = sink.Write(data);
        Assert.AreEqual(V3IO.DataTransferStatus.IoError, failed.Status);
        Assert.AreEqual(2L, failed.BytesTransferred);
        Assert.IsTrue(failed.IsByteCountExact);
        Assert.AreEqual(2L, sink.Position);

        V3IO.DataTransferResult resumed = sink.Write(data.AsSpan(2));
        Assert.AreEqual(V3IO.DataTransferStatus.Success, resumed.Status);
        Assert.AreEqual(2L, resumed.BytesTransferred);
        CollectionAssert.AreEqual(data, stream.ToArray());

        using UnreportablePartialWriteMemoryStream unreportableStream = new(prefixLength: 2);
        using V3IO.StreamDataSink unreportableSink = new(unreportableStream, leaveOpen: true);
        V3IO.DataTransferResult unknown = unreportableSink.Write(data);
        Assert.AreEqual(V3IO.DataTransferStatus.IoError, unknown.Status);
        Assert.AreEqual(0L, unknown.BytesTransferred);
        Assert.IsFalse(unknown.IsByteCountExact);
        Assert.AreEqual(2L, unreportableSink.Position);
        Assert.AreEqual(
            V3IO.DataTransferStatus.Success,
            unreportableSink.Write(data.AsSpan(2)).Status);
        CollectionAssert.AreEqual(data, unreportableStream.ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 stream sink reports asynchronous partial cancellation and remains reusable")]
    public async Task Case_V3DataSource_StreamSinkReportsAsynchronousPartialCancellationAndRemainsReusable()
    {
        await using PartialCancelWriteMemoryStream stream = new(prefixLength: 2);
        await using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        byte[] data = new byte[] { 5, 6, 7, 8 };

        V3IO.DataTransferResult canceled = await sink.WriteAsync(data);
        Assert.AreEqual(V3IO.DataTransferStatus.Canceled, canceled.Status);
        Assert.AreEqual(2L, canceled.BytesTransferred);
        Assert.IsTrue(canceled.IsByteCountExact);
        Assert.AreEqual(2L, sink.Position);

        V3IO.DataTransferResult resumed = await sink.WriteAsync(data.AsMemory(2));
        Assert.AreEqual(V3IO.DataTransferStatus.Success, resumed.Status);
        Assert.AreEqual(2L, resumed.BytesTransferred);
        CollectionAssert.AreEqual(data, stream.ToArray());
    }

    private sealed class ChunkedMemoryStream : MemoryStream
    {
        private readonly int _maximumRead;

        public ChunkedMemoryStream(byte[] data, int maximumRead)
            : base(data, writable: false)
        {
            _maximumRead = maximumRead;
        }

        public int ReadCallCount { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            ReadCallCount++;
            return base.Read(buffer.Slice(0, Math.Min(buffer.Length, _maximumRead)));
        }
    }

    private sealed class AsyncChunkedMemoryStream : MemoryStream
    {
        private readonly byte[] _data;
        private readonly int _maximumRead;
        private readonly TaskCompletionSource<bool> _readsReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public AsyncChunkedMemoryStream(byte[] data, int maximumRead)
            : base(data, writable: false)
        {
            _data = data;
            _maximumRead = maximumRead;
        }

        public int SyncReadCallCount { get; private set; }

        public int AsyncReadCallCount { get; private set; }

        public void ReleaseReads()
        {
            _readsReleased.TrySetResult(true);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            SyncReadCallCount++;
            throw new InvalidOperationException("The async adapter called synchronous Read.");
        }

        public override int Read(Span<byte> buffer)
        {
            SyncReadCallCount++;
            throw new InvalidOperationException("The async adapter called synchronous Read.");
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            AsyncReadCallCount++;
            await _readsReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            int count = checked((int)Math.Min(
                Math.Min(buffer.Length, _maximumRead),
                Length - Position));
            _data.AsMemory(checked((int)Position), count).CopyTo(buffer);
            Position = checked(Position + count);
            return count;
        }
    }

    private sealed class NonSeekableReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingReadStream : MemoryStream
    {
        public ThrowingReadStream(byte[] data)
            : base(data, writable: false)
        {
        }

        public override int Read(Span<byte> buffer)
        {
            throw new IOException("Injected read failure.");
        }
    }

    private sealed class AsyncOnlyWriteMemoryStream : MemoryStream
    {
        private readonly TaskCompletionSource<bool> _writesReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int SyncWriteCallCount { get; private set; }

        public int AsyncWriteCallCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void ReleaseWrites()
        {
            _writesReleased.TrySetResult(true);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            SyncWriteCallCount++;
            throw new InvalidOperationException("The async adapter called synchronous Write.");
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            SyncWriteCallCount++;
            throw new InvalidOperationException("The async adapter called synchronous Write.");
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            AsyncWriteCallCount++;
            await _writesReleased.Task.ConfigureAwait(false);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            byte[] copy = buffer.ToArray();
            base.Write(copy, 0, copy.Length);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class PartialFailingWriteMemoryStream : MemoryStream
    {
        private readonly int _prefixLength;
        private bool _failNextWrite = true;

        public PartialFailingWriteMemoryStream(int prefixLength)
        {
            _prefixLength = prefixLength;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_failNextWrite)
            {
                _failNextWrite = false;
                base.Write(buffer.Slice(0, Math.Min(buffer.Length, _prefixLength)));
                throw new IOException("Injected failure after a partial write.");
            }

            base.Write(buffer);
        }
    }

    private sealed class PartialCancelWriteMemoryStream : MemoryStream
    {
        private readonly int _prefixLength;
        private bool _cancelNextWrite = true;

        public PartialCancelWriteMemoryStream(int prefixLength)
        {
            _prefixLength = prefixLength;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_cancelNextWrite)
            {
                _cancelNextWrite = false;
                base.Write(buffer.Span.Slice(0, Math.Min(buffer.Length, _prefixLength)));
                throw new OperationCanceledException(cancellationToken);
            }

            base.Write(buffer.Span);
        }
    }

    private sealed class UnreportablePartialWriteMemoryStream : MemoryStream
    {
        private readonly int _prefixLength;
        private bool _failNextWrite = true;
        private bool _failNextPositionRead;

        public UnreportablePartialWriteMemoryStream(int prefixLength)
        {
            _prefixLength = prefixLength;
        }

        public override long Position
        {
            get
            {
                if (_failNextPositionRead)
                {
                    _failNextPositionRead = false;
                    throw new IOException("Injected Position failure after a partial write.");
                }

                return base.Position;
            }
            set => base.Position = value;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_failNextWrite)
            {
                _failNextWrite = false;
                base.Write(buffer.Slice(0, Math.Min(buffer.Length, _prefixLength)));
                _failNextPositionRead = true;
                throw new IOException("Injected failure after a partial write.");
            }

            base.Write(buffer);
        }
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}: {exception.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}: {exception.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
