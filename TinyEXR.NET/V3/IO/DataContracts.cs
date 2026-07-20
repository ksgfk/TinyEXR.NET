using System;
using System.Threading;
using System.Threading.Tasks;

namespace TinyEXR.V3.IO
{
    /// <summary>
    /// Result of an exact source read or sink operation.
    /// </summary>
    public enum DataTransferStatus
    {
        /// <summary>The requested range or sink operation completed in full.</summary>
        Success = 0,

        /// <summary>A source returned EOF before the exact range completed.</summary>
        EndOfSource = 1,

        /// <summary>An incremental source lacks PendingRange and did not modify the destination.</summary>
        WouldBlock = 2,

        /// <summary>The cancellation token stopped the operation; Error is an OperationCanceledException.</summary>
        Canceled = 3,

        /// <summary>The adapter or underlying object is closed; Error is an ObjectDisposedException.</summary>
        Disposed = 4,

        /// <summary>The underlying Stream raised an I/O failure, retained in Error.</summary>
        IoError = 5,
    }

    /// <summary>
    /// An absolute, half-open byte range.
    /// </summary>
    public readonly struct DataRange
    {
        public DataRange(long offset, long length)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            End = checked(offset + length);
            Offset = offset;
            Length = length;
        }

        public long Offset { get; }

        public long Length { get; }

        public long End { get; }
    }

    /// <summary>
    /// Status, progress, and retry information for one data operation.
    /// </summary>
    public readonly struct DataTransferResult
    {
        private DataTransferResult(
            DataTransferStatus status,
            long bytesTransferred,
            bool isByteCountExact,
            DataRange? pendingRange,
            Exception? error)
        {
            if (bytesTransferred < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesTransferred));
            }

            Status = status;
            BytesTransferred = bytesTransferred;
            IsByteCountExact = isByteCountExact;
            PendingRange = pendingRange;
            Error = error;
        }

        public DataTransferStatus Status { get; }

        /// <summary>
        /// Confirmed bytes transferred before the operation returned. When
        /// <see cref="IsByteCountExact"/> is false, additional bytes may also have been
        /// transferred by the underlying stream.
        /// </summary>
        public long BytesTransferred { get; }

        /// <summary>
        /// Whether BytesTransferred is the exact observed progress rather than a confirmed lower bound.
        /// </summary>
        public bool IsByteCountExact { get; }

        /// <summary>
        /// First missing range for <see cref="DataTransferStatus.WouldBlock"/>.
        /// </summary>
        public DataRange? PendingRange { get; }

        /// <summary>
        /// Captured cancellation, disposal, or I/O exception. Argument errors are thrown directly.
        /// </summary>
        public Exception? Error { get; }

        public bool IsSuccess => Status == DataTransferStatus.Success;

        public static DataTransferResult Success(long bytesTransferred)
        {
            return new DataTransferResult(DataTransferStatus.Success, bytesTransferred, true, null, null);
        }

        public static DataTransferResult EndOfSource(long bytesTransferred)
        {
            return new DataTransferResult(DataTransferStatus.EndOfSource, bytesTransferred, true, null, null);
        }

        public static DataTransferResult WouldBlock(DataRange pendingRange)
        {
            return new DataTransferResult(DataTransferStatus.WouldBlock, 0, true, pendingRange, null);
        }

        public static DataTransferResult Canceled(
            long bytesTransferred,
            OperationCanceledException error,
            bool isByteCountExact = true)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new DataTransferResult(
                DataTransferStatus.Canceled,
                bytesTransferred,
                isByteCountExact,
                null,
                error);
        }

        public static DataTransferResult Disposed(
            long bytesTransferred,
            ObjectDisposedException error,
            bool isByteCountExact = true)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new DataTransferResult(
                DataTransferStatus.Disposed,
                bytesTransferred,
                isByteCountExact,
                null,
                error);
        }

        public static DataTransferResult IoError(
            long bytesTransferred,
            Exception error,
            bool isByteCountExact = true)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new DataTransferResult(
                DataTransferStatus.IoError,
                bytesTransferred,
                isByteCountExact,
                null,
                error);
        }
    }

    /// <summary>
    /// Length discovery shared by synchronous and asynchronous sources. Length throws when
    /// HasKnownLength is false; state machines should use TryGetLength for streaming inputs.
    /// </summary>
    public interface IDataSourceLength
    {
        bool HasKnownLength { get; }

        long Length { get; }

        bool TryGetLength(out long length);
    }

    /// <summary>
    /// Random-access source whose successful reads always fill the entire destination.
    /// </summary>
    public interface IExactDataSource : IDataSourceLength
    {
        DataTransferResult ReadExactly(long offset, Span<byte> destination);
    }

    /// <summary>
    /// Asynchronous random-access source whose successful reads always fill the entire destination.
    /// </summary>
    public interface IAsyncExactDataSource : IDataSourceLength
    {
        ValueTask<DataTransferResult> ReadExactlyAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Seekable synchronous output used by a writer for forward writes and header backpatches.
    /// </summary>
    public interface ISeekableDataSink : IDisposable
    {
        long Position { get; }

        DataTransferResult Write(ReadOnlySpan<byte> source);

        DataTransferResult Seek(long offset);

        DataTransferResult Flush();
    }

    /// <summary>
    /// Seekable asynchronous output. Seek itself has no asynchronous Stream primitive;
    /// SeekAsync only waits asynchronously for serialized access before performing it.
    /// </summary>
    public interface IAsyncSeekableDataSink : IAsyncDisposable
    {
        long Position { get; }

        ValueTask<DataTransferResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default);

        ValueTask<DataTransferResult> SeekAsync(
            long offset,
            CancellationToken cancellationToken = default);

        ValueTask<DataTransferResult> FlushAsync(CancellationToken cancellationToken = default);
    }

    internal static class DataRangeValidation
    {
        public static long GetEnd(long offset, long length)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            return checked(offset + length);
        }
    }
}
