using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TinyEXR.V3.IO
{
    /// <summary>
    /// Seekable output adapter suitable for forward writes and header backpatches. Operations
    /// are serialized around the shared Stream.Position. Dispose/DisposeAsync flush the stream;
    /// leaveOpen controls whether they also close the caller-owned stream.
    /// </summary>
    public sealed class StreamDataSink :
        ISeekableDataSink,
        IAsyncSeekableDataSink,
        IDisposable,
        IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly long _origin;
        private readonly bool _leaveOpen;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public StreamDataSink(Stream stream, bool leaveOpen = false)
            : this(stream, 0, leaveOpen)
        {
        }

        /// <summary>
        /// Creates a seekable output view whose logical offset zero is <paramref name="origin"/>.
        /// The stream is positioned at that origin before the first write.
        /// </summary>
        public StreamDataSink(Stream stream, long origin, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (origin < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }

            if (!stream.CanWrite)
            {
                throw new ArgumentException("The stream must be writable.", nameof(stream));
            }

            if (!stream.CanSeek)
            {
                throw new ArgumentException("The stream must be seekable.", nameof(stream));
            }

            _origin = origin;
            _leaveOpen = leaveOpen;
            stream.Seek(origin, SeekOrigin.Begin);
        }

        public long Position
        {
            get
            {
                _gate.Wait();
                try
                {
                    ThrowIfDisposed();
                    return checked(_stream.Position - _origin);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        public DataTransferResult Write(ReadOnlySpan<byte> source)
        {
            _gate.Wait();
            long startPosition = 0;
            bool writeInvoked = false;
            try
            {
                if (_disposed)
                {
                    return Disposed();
                }

                startPosition = _stream.Position;
                _ = checked(startPosition + source.Length);

                writeInvoked = true;
                _stream.Write(source);
                return DataTransferResult.Success(source.Length);
            }
            catch (ObjectDisposedException exception)
            {
                TransferProgress progress = MeasureWriteProgress(startPosition, source.Length, writeInvoked);
                return DataTransferResult.Disposed(
                    progress.BytesTransferred,
                    exception,
                    progress.IsExact);
            }
            catch (IOException exception)
            {
                TransferProgress progress = MeasureWriteProgress(startPosition, source.Length, writeInvoked);
                return DataTransferResult.IoError(
                    progress.BytesTransferred,
                    exception,
                    progress.IsExact);
            }
            finally
            {
                _gate.Release();
            }
        }

        public DataTransferResult Seek(long offset)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            _gate.Wait();
            try
            {
                if (_disposed)
                {
                    return Disposed();
                }

                long physicalOffset = checked(_origin + offset);
                long actual = _stream.Seek(physicalOffset, SeekOrigin.Begin);
                if (actual != physicalOffset)
                {
                    return DataTransferResult.IoError(
                        0,
                        new IOException($"The stream sought to {actual} instead of {physicalOffset}."));
                }

                return DataTransferResult.Success(0);
            }
            catch (ObjectDisposedException exception)
            {
                return DataTransferResult.Disposed(0, exception);
            }
            catch (IOException exception)
            {
                return DataTransferResult.IoError(0, exception);
            }
            finally
            {
                _gate.Release();
            }
        }

        public DataTransferResult Flush()
        {
            _gate.Wait();
            try
            {
                if (_disposed)
                {
                    return Disposed();
                }

                _stream.Flush();
                return DataTransferResult.Success(0);
            }
            catch (ObjectDisposedException exception)
            {
                return DataTransferResult.Disposed(0, exception);
            }
            catch (IOException exception)
            {
                return DataTransferResult.IoError(0, exception);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<DataTransferResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            DataTransferResult? gateFailure = await EnterAsync(cancellationToken).ConfigureAwait(false);
            if (gateFailure.HasValue)
            {
                return gateFailure.Value;
            }

            long startPosition = 0;
            bool writeInvoked = false;
            try
            {
                if (_disposed)
                {
                    return Disposed();
                }

                startPosition = _stream.Position;
                _ = checked(startPosition + source.Length);

                writeInvoked = true;
                await _stream.WriteAsync(source, cancellationToken).ConfigureAwait(false);
                return DataTransferResult.Success(source.Length);
            }
            catch (OperationCanceledException exception)
            {
                TransferProgress progress = MeasureWriteProgress(startPosition, source.Length, writeInvoked);
                return DataTransferResult.Canceled(
                    progress.BytesTransferred,
                    exception,
                    progress.IsExact);
            }
            catch (ObjectDisposedException exception)
            {
                TransferProgress progress = MeasureWriteProgress(startPosition, source.Length, writeInvoked);
                return DataTransferResult.Disposed(
                    progress.BytesTransferred,
                    exception,
                    progress.IsExact);
            }
            catch (IOException exception)
            {
                TransferProgress progress = MeasureWriteProgress(startPosition, source.Length, writeInvoked);
                return DataTransferResult.IoError(
                    progress.BytesTransferred,
                    exception,
                    progress.IsExact);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<DataTransferResult> SeekAsync(
            long offset,
            CancellationToken cancellationToken = default)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            DataTransferResult? gateFailure = await EnterAsync(cancellationToken).ConfigureAwait(false);
            if (gateFailure.HasValue)
            {
                return gateFailure.Value;
            }

            try
            {
                if (_disposed)
                {
                    return Disposed();
                }

                long physicalOffset = checked(_origin + offset);
                long actual = _stream.Seek(physicalOffset, SeekOrigin.Begin);
                if (actual != physicalOffset)
                {
                    return DataTransferResult.IoError(
                        0,
                        new IOException($"The stream sought to {actual} instead of {physicalOffset}."));
                }

                return DataTransferResult.Success(0);
            }
            catch (ObjectDisposedException exception)
            {
                return DataTransferResult.Disposed(0, exception);
            }
            catch (IOException exception)
            {
                return DataTransferResult.IoError(0, exception);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<DataTransferResult> FlushAsync(
            CancellationToken cancellationToken = default)
        {
            DataTransferResult? gateFailure = await EnterAsync(cancellationToken).ConfigureAwait(false);
            if (gateFailure.HasValue)
            {
                return gateFailure.Value;
            }

            try
            {
                if (_disposed)
                {
                    return Disposed();
                }

                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return DataTransferResult.Success(0);
            }
            catch (OperationCanceledException exception)
            {
                return DataTransferResult.Canceled(0, exception);
            }
            catch (ObjectDisposedException exception)
            {
                return DataTransferResult.Disposed(0, exception);
            }
            catch (IOException exception)
            {
                return DataTransferResult.IoError(0, exception);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _gate.Wait();
            try
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try
                {
                    _stream.Flush();
                }
                finally
                {
                    if (!_leaveOpen)
                    {
                        _stream.Dispose();
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try
                {
                    await _stream.FlushAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (!_leaveOpen)
                    {
                        await _stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async ValueTask<DataTransferResult?> EnterAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return DataTransferResult.Canceled(0, new OperationCanceledException(cancellationToken));
            }

            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException exception)
            {
                return DataTransferResult.Canceled(0, exception);
            }
        }

        private static DataTransferResult Disposed()
        {
            return DataTransferResult.Disposed(0, new ObjectDisposedException(nameof(StreamDataSink)));
        }

        private TransferProgress MeasureWriteProgress(
            long startPosition,
            int requestedLength,
            bool writeInvoked)
        {
            if (!writeInvoked)
            {
                return new TransferProgress(0, isExact: true);
            }

            try
            {
                long delta = checked(_stream.Position - startPosition);
                if (delta >= 0 && delta <= requestedLength)
                {
                    return new TransferProgress(delta, isExact: true);
                }
            }
            catch (Exception)
            {
                // The original write failure remains authoritative. A stream that no longer
                // exposes Position cannot provide an exact partial-write count.
            }

            return new TransferProgress(0, isExact: false);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(StreamDataSink));
            }
        }

        private readonly struct TransferProgress
        {
            public TransferProgress(long bytesTransferred, bool isExact)
            {
                BytesTransferred = bytesTransferred;
                IsExact = isExact;
            }

            public long BytesTransferred { get; }

            public bool IsExact { get; }
        }
    }
}
