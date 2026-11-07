using System;
using System.Buffers;
using TinyEXR.Codecs;

namespace TinyEXR
{
    /// <summary>Private pooled buffers shared by one reader/writer or a reusable ExrContext.</summary>
    internal sealed class CodecResources : IDisposable
    {
        // ArrayPool<T>.Create() defaults to a 1 MiB maximum array length. EXR blocks and their payloads routinely
        // exceed that, and oversized rentals bypass the pool entirely, so raise the limit while keeping retention
        // bounded to a few arrays per size class.
        private const int MaximumPooledByteLength = 1 << 28;
        private const int MaximumArraysPerBucket = 4;

        private ArrayPool<byte>? _bytePool =
            ArrayPool<byte>.Create(MaximumPooledByteLength, MaximumArraysPerBucket);
        private CodecWorkspace? _codecWorkspace =
            new CodecWorkspace();
        private Codecs.Htj2kDecoder.Htj2kBufferPool? _htj2kPool =
            new Codecs.Htj2kDecoder.Htj2kBufferPool();

        /// <summary>The codec scratch workspace reused across blocks by the V3 compression codecs.</summary>
        public CodecWorkspace CodecWorkspace => _codecWorkspace ??
            throw new ObjectDisposedException(nameof(CodecResources));

        /// <summary>The HTJ2K coefficient-plane pool reused across blocks.</summary>
        public Codecs.Htj2kDecoder.Htj2kBufferPool Htj2kPool => _htj2kPool ??
            throw new ObjectDisposedException(nameof(CodecResources));

        public byte[] RentBytes(int minimumLength)
        {
            ArrayPool<byte> pool = _bytePool ??
                throw new ObjectDisposedException(nameof(CodecResources));
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
