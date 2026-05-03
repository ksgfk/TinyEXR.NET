using System.IO;
#if NETSTANDARD2_1
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
#else
using System.IO.Compression;
#endif

namespace TinyEXR.PortV1
{
    internal static class ZlibCompat
    {
        public static byte[] Compress(ReadOnlySpan<byte> raw)
        {
            using MemoryStream output = new MemoryStream(raw.Length);

#if NETSTANDARD2_1
            byte[] buffer = raw.ToArray();
            using (DeflaterOutputStream zlib = new DeflaterOutputStream(output, new Deflater(9, noZlibHeaderOrFooter: false), 512))
            {
                zlib.IsStreamOwner = false;
                zlib.Write(buffer, 0, buffer.Length);
                zlib.Finish();
            }
#else
            using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }
#endif

            return output.ToArray();
        }

        public static bool TryDecompress(byte[] payload, byte[] destination, int expectedSize)
        {
            return TryDecompress(payload, payload.Length, destination, expectedSize);
        }

        public static bool TryDecompress(byte[] payload, int payloadLength, byte[] destination, int expectedSize)
        {
            if (expectedSize < 0 || expectedSize > destination.Length)
            {
                return false;
            }

            if (payloadLength < 0 || payloadLength > payload.Length)
            {
                return false;
            }

            using MemoryStream input = new MemoryStream(payload, 0, payloadLength, writable: false);

#if NETSTANDARD2_1
            using InflaterInputStream zlib = new InflaterInputStream(input, new Inflater(noHeader: false), 512);
            zlib.IsStreamOwner = false;
#else
            using ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress);
#endif

            int total = 0;
            while (total < expectedSize)
            {
                int read = zlib.Read(destination, total, expectedSize - total);
                if (read <= 0)
                {
                    return false;
                }

                total += read;
            }

#if NETSTANDARD2_1
            byte[] extraByteBuffer = new byte[1];
            return zlib.Read(extraByteBuffer, 0, 1) == 0;
#else
            Span<byte> extraByteBuffer = stackalloc byte[1];
            return zlib.Read(extraByteBuffer) == 0;
#endif
        }

        public static byte[] Decompress(ReadOnlySpan<byte> payload)
        {
            using MemoryStream input = new MemoryStream(payload.ToArray(), writable: false);
            using MemoryStream output = new MemoryStream();

#if NETSTANDARD2_1
            using (InflaterInputStream zlib = new InflaterInputStream(input, new Inflater(noHeader: false), 512))
            {
                zlib.IsStreamOwner = false;
                zlib.CopyTo(output);
            }
#else
            using (ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress))
            {
                zlib.CopyTo(output);
            }
#endif

            return output.ToArray();
        }

    }
}
