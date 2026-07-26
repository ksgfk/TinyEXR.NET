using System;
using TinyEXR.PortV1;
using TinyEXR.V3.Codecs;

namespace TinyEXR.V3
{
    internal static class DeepPayloadDecoder
    {
        public static ReaderResult? Decode(
            Compression compression,
            byte[] payload,
            int expectedSize,
            out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            if (expectedSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedSize));
            }

            if (expectedSize == 0)
            {
                return null;
            }

            if (payload.Length == expectedSize)
            {
                decoded = payload;
                return null;
            }

            if (compression == Compression.None ||
                compression == Compression.HTJ2K256 ||
                compression == Compression.HTJ2K32)
            {
                return Corrupt($"Compression '{compression}' cannot encode a non-raw deep payload.");
            }

            if (compression != Compression.RLE &&
                compression != Compression.ZIPS &&
                compression != Compression.ZIP)
            {
                return Unsupported($"Compression '{compression}' is not permitted for compressed deep payloads.");
            }

            ResultCode result = ExrCompressionCodec.TryDecodeDeepPayload(
                (CompressionType)(int)compression,
                payload,
                expectedSize,
                out decoded);
            if (result == ResultCode.Success && decoded.Length == expectedSize)
            {
                return null;
            }

            decoded = Array.Empty<byte>();
            if (result == ResultCode.UnsupportedFeature || result == ResultCode.UnsupportedFormat)
            {
                return Unsupported($"Compression '{compression}' is not supported for deep payloads.");
            }

            return Corrupt($"The compressed deep payload could not be decoded ({result}).");
        }

        private static ReaderResult Corrupt(string message)
        {
            return new ReaderResult(
                ExrResult.Corrupt,
                null,
                new InvalidOperationException(message));
        }

        private static ReaderResult Unsupported(string message)
        {
            return new ReaderResult(
                ExrResult.Unsupported,
                null,
                new NotSupportedException(message));
        }
    }
}
