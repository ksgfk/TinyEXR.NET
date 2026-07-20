using System;
using System.Threading;
using System.Threading.Tasks;

namespace TinyEXR.V3.IO
{
    /// <summary>
    /// Exact source over caller-owned memory. Construction and TryGetMemory are zero-copy;
    /// callers must keep the supplied memory stable for the source lifetime. ReadExactly
    /// copies into the requested destination.
    /// </summary>
    public sealed class MemoryDataSource : IExactDataSource, IAsyncExactDataSource
    {
        private readonly ReadOnlyMemory<byte> _data;

        public MemoryDataSource(ReadOnlyMemory<byte> data)
        {
            _data = data;
        }

        public long Length => _data.Length;

        public bool HasKnownLength => true;

        public bool TryGetLength(out long length)
        {
            length = Length;
            return true;
        }

        public DataTransferResult ReadExactly(long offset, Span<byte> destination)
        {
            long end = DataRangeValidation.GetEnd(offset, destination.Length);
            if (end > Length)
            {
                return DataTransferResult.EndOfSource(0);
            }

            _data.Span.Slice(checked((int)offset), destination.Length).CopyTo(destination);
            return DataTransferResult.Success(destination.Length);
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

        /// <summary>
        /// Returns a zero-copy slice when the complete range is available.
        /// </summary>
        public bool TryGetMemory(long offset, long length, out ReadOnlyMemory<byte> memory)
        {
            long end = DataRangeValidation.GetEnd(offset, length);
            if (end > Length)
            {
                memory = default;
                return false;
            }

            memory = _data.Slice(checked((int)offset), checked((int)length));
            return true;
        }
    }
}
