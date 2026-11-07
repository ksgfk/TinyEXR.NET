using System;
using TinyEXR.Codecs;

namespace TinyEXR
{
    internal static class DeepPayloadDecoder
    {
        public static ReaderResult? Decode(
            Compression compression,
            byte[] payload,
            int expectedSize,
            CodecWorkspace workspace,
            out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            if (expectedSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedSize));
            }

            if (compression != Compression.None && compression != Compression.RLE && compression != Compression.ZIPS)
                return Unsupported($"Compression '{compression}' is not supported for deep payloads.");
            if (expectedSize == 0)
                return payload.Length == 0 ? null : Corrupt("An empty deep block has unexpected sample data.");

            if (payload.Length == expectedSize)
            {
                decoded = payload;
                return null;
            }

            if (compression == Compression.None)
            {
                return Corrupt($"Compression '{compression}' cannot encode a non-raw deep payload.");
            }

            decoded = new byte[expectedSize];
            ExrResult result = BlockCodec.DecodeDeep(compression, payload, decoded, workspace);
            if (result == ExrResult.Success)
                return null;
            decoded = Array.Empty<byte>();
            return new ReaderResult(result, null, new InvalidDataException($"The compressed deep payload could not be decoded ({result})."));
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
