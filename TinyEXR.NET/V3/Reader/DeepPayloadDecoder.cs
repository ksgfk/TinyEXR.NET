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

            if (compression == Compression.ZSTD)
            {
                decoded = new byte[expectedSize];
                ZstdFrameStatus status = ZstdFrameDecoder.Decode(
                    payload,
                    decoded,
                    out int consumed,
                    out int written,
                    out _);
                if (status == ZstdFrameStatus.Success &&
                    consumed == payload.Length &&
                    written == expectedSize)
                {
                    return null;
                }

                decoded = Array.Empty<byte>();
                if (status == ZstdFrameStatus.DictionaryNotSupported ||
                    status == ZstdFrameStatus.WindowTooLarge ||
                    status == ZstdFrameStatus.ContentSizeTooLarge ||
                    status == ZstdFrameStatus.UnsupportedCompressedBlock)
                {
                    return Unsupported($"The ZSTD deep payload is not supported ({status}).");
                }

                return Corrupt($"The ZSTD deep payload is invalid ({status}).");
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
