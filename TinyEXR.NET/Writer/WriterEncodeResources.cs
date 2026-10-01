using System;
using System.Buffers;
using TinyEXR.PortV1;

namespace TinyEXR
{
    /// <summary>
    /// The buffer and workspace resources shared by every block encode of one <see cref="ExrWriter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TinyEXR.NET never uses <see cref="ArrayPool{T}.Shared"/>. Each writer creates its own pool with
    /// <see cref="ArrayPool{T}.Create()"/>, so the library neither retains buffers in process-wide state nor
    /// competes with host application code for shared pool buckets. Disposing the writer drops the pool
    /// reference and every array it retained, leaving no residue behind.
    /// </para>
    /// <para>
    /// A resource set is not thread-safe. It belongs to exactly one writer, whose state machine serializes all
    /// buffer traffic.
    /// </para>
    /// </remarks>
    internal sealed class WriterEncodeResources : IDisposable
    {
        // ArrayPool<T>.Create() defaults to a 1 MiB maximum array length. EXR chunks routinely exceed that, and
        // oversized rentals bypass the pool entirely, so raise the limit while keeping retention bounded to a few
        // arrays per size class.
        private const int MaximumPooledByteLength = 1 << 28;
        private const int MaximumArraysPerBucket = 4;

        private ArrayPool<byte>? _bytePool =
            ArrayPool<byte>.Create(MaximumPooledByteLength, MaximumArraysPerBucket);
        private ExrCompressionCodec.EncodeWorkspace? _codecWorkspace =
            new ExrCompressionCodec.EncodeWorkspace();
        private Codecs.Htj2kDecoder.Htj2kBufferPool? _htj2kPool =
            new Codecs.Htj2kDecoder.Htj2kBufferPool();

        /// <summary>The codec scratch workspace reused across blocks by the PortV1 compression codecs.</summary>
        public ExrCompressionCodec.EncodeWorkspace CodecWorkspace => _codecWorkspace ??
            throw new ObjectDisposedException(nameof(WriterEncodeResources));

        /// <summary>The HTJ2K coefficient-plane pool reused across blocks.</summary>
        public Codecs.Htj2kDecoder.Htj2kBufferPool Htj2kPool => _htj2kPool ??
            throw new ObjectDisposedException(nameof(WriterEncodeResources));

        public byte[] RentBytes(int minimumLength)
        {
            ArrayPool<byte> pool = _bytePool ??
                throw new ObjectDisposedException(nameof(WriterEncodeResources));
            return minimumLength == 0 ? Array.Empty<byte>() : pool.Rent(minimumLength);
        }

        public void ReturnBytes(byte[]? array)
        {
            if (array == null || array.Length == 0)
            {
                return;
            }

            _bytePool?.Return(array);
        }

        public void Dispose()
        {
            // Drop every pool and workspace so their retained arrays become collectable immediately.
            _bytePool = null;
            _codecWorkspace = null;
            _htj2kPool?.Dispose();
            _htj2kPool = null;
        }
    }
}
