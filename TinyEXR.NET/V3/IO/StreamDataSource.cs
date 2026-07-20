using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TinyEXR.V3.IO
{
    /// <summary>
    /// Exact random-access adapter over a readable, seekable Stream. All sync and async
    /// operations are serialized because Stream.Position is shared. Each operation seeks
    /// to its absolute offset and leaves Position immediately after the bytes transferred;
    /// callers must not access the Stream concurrently outside this adapter.
    /// </summary>
    public sealed class StreamDataSource : IExactDataSource, IAsyncExactDataSource, IDisposable, IAsyncDisposable
    {
        private readonly Stream _stream;
        private readonly long _origin;
        private readonly bool _leaveOpen;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public StreamDataSource(Stream stream, bool leaveOpen = false)
            : this(stream, 0, leaveOpen)
        {
        }

        /// <summary>
        /// Creates a random-access view whose logical offset zero is <paramref name="origin"/>.
        /// </summary>
        public StreamDataSource(Stream stream, long origin, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (origin < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }

            if (!stream.CanRead)
            {
                throw new ArgumentException("The stream must be readable.", nameof(stream));
            }

            if (!stream.CanSeek)
            {
                throw new ArgumentException("The stream must be seekable.", nameof(stream));
            }

            if (origin > stream.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(origin), "The logical origin lies beyond the stream length.");
            }

            _origin = origin;
            _leaveOpen = leaveOpen;
        }

        public long Length
        {
            get
            {
                _gate.Wait();
                try
                {
                    ThrowIfDisposed();
                    return checked(_stream.Length - _origin);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        public bool HasKnownLength => true;

        public bool TryGetLength(out long length)
        {
            length = Length;
            return true;
        }

        public DataTransferResult ReadExactly(long offset, Span<byte> destination)
        {
            DataRangeValidation.GetEnd(offset, destination.Length);
            _gate.Wait();
            int bytesRead = 0;
            try
            {
                if (_disposed)
                {
                    return Disposed(bytesRead);
                }

                _stream.Seek(checked(_origin + offset), SeekOrigin.Begin);
                while (bytesRead < destination.Length)
                {
                    int count = _stream.Read(destination.Slice(bytesRead));
                    if (count == 0)
                    {
                        return DataTransferResult.EndOfSource(bytesRead);
                    }

                    bytesRead = checked(bytesRead + count);
                }

                return DataTransferResult.Success(bytesRead);
            }
            catch (ObjectDisposedException exception)
            {
                return DataTransferResult.Disposed(bytesRead, exception, isByteCountExact: false);
            }
            catch (IOException exception)
            {
                return DataTransferResult.IoError(bytesRead, exception, isByteCountExact: false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<DataTransferResult> ReadExactlyAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            DataRangeValidation.GetEnd(offset, destination.Length);
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(0, cancellationToken);
            }

            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                return DataTransferResult.Canceled(0, exception);
            }

            int bytesRead = 0;
            try
            {
                if (_disposed)
                {
                    return Disposed(bytesRead);
                }

                _stream.Seek(checked(_origin + offset), SeekOrigin.Begin);
                while (bytesRead < destination.Length)
                {
                    int count = await _stream.ReadAsync(
                        destination.Slice(bytesRead),
                        cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        return DataTransferResult.EndOfSource(bytesRead);
                    }

                    bytesRead = checked(bytesRead + count);
                }

                return DataTransferResult.Success(bytesRead);
            }
            catch (OperationCanceledException exception)
            {
                return DataTransferResult.Canceled(bytesRead, exception, isByteCountExact: false);
            }
            catch (ObjectDisposedException exception)
            {
                return DataTransferResult.Disposed(bytesRead, exception, isByteCountExact: false);
            }
            catch (IOException exception)
            {
                return DataTransferResult.IoError(bytesRead, exception, isByteCountExact: false);
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
                if (!_leaveOpen)
                {
                    _stream.Dispose();
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
                if (!_leaveOpen)
                {
                    await _stream.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private static DataTransferResult Canceled(long bytesRead, CancellationToken cancellationToken)
        {
            return DataTransferResult.Canceled(bytesRead, new OperationCanceledException(cancellationToken));
        }

        private static DataTransferResult Disposed(long bytesRead)
        {
            return DataTransferResult.Disposed(
                bytesRead,
                new ObjectDisposedException(nameof(StreamDataSource)));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(StreamDataSource));
            }
        }
    }
}
