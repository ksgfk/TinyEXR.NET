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
            using MemoryStream output = new MemoryStream();
            byte[] buffer = raw.ToArray();

#if NETSTANDARD2_1
            using (DeflaterOutputStream zlib = new DeflaterOutputStream(output, new Deflater(9, noZlibHeaderOrFooter: false), 512))
            {
                zlib.IsStreamOwner = false;
                zlib.Write(buffer, 0, buffer.Length);
                zlib.Finish();
            }
#else
            using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(buffer, 0, buffer.Length);
            }
#endif

            return output.ToArray();
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
