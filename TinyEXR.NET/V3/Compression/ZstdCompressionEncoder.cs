using System;
using ZstdSharp;

namespace TinyEXR.V3.Codecs
{
    /// <summary>
    /// Reusable one-shot Zstandard encoder matching tinyexr's level-3 policy.
    /// </summary>
    internal sealed class ZstdCompressionEncoder : IDisposable
    {
        private const int CompressionLevel = 3;

        private Compressor? _compressor;
        private bool _disposed;

        internal byte[] Encode(ReadOnlySpan<byte> source)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ZstdCompressionEncoder));
            }

            if (source.IsEmpty)
            {
                return Array.Empty<byte>();
            }

            try
            {
                _compressor ??= new Compressor(CompressionLevel);
                int bound = Compressor.GetCompressBound(source.Length);
                if (bound <= 0)
                {
                    throw new ZstdCompressionException("Zstandard returned an invalid compression bound.");
                }

                byte[] encoded = new byte[bound];
                int written = _compressor.Wrap(source, encoded);
                if (written <= 0 || written > encoded.Length)
                {
                    throw new ZstdCompressionException("Zstandard returned an invalid encoded length.");
                }

                if (written >= source.Length)
                {
                    return source.ToArray();
                }

                Array.Resize(ref encoded, written);
                return encoded;
            }
            catch (ZstdException exception)
            {
                throw new ZstdCompressionException("Zstandard compression failed.", exception);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _compressor?.Dispose();
            _compressor = null;
            _disposed = true;
        }
    }

    internal sealed class ZstdCompressionException : Exception
    {
        internal ZstdCompressionException(string message)
            : base(message)
        {
        }

        internal ZstdCompressionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
