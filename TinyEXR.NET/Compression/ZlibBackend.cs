using System.Buffers.Binary;
using System.IO.Compression;
#if NETSTANDARD2_1
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
#endif

namespace TinyEXR.Codecs
{
    /// <summary>Managed implementation of the v3 zlib backend contract. Inflation is always bounded.</summary>
    internal static class ZlibBackend
    {
        public static byte[] Deflate(ReadOnlySpan<byte> source)
        {
            using MemoryStream output = new MemoryStream();
#if NETSTANDARD2_1
            using (DeflaterOutputStream stream = new DeflaterOutputStream(output, new Deflater(9)))
            {
                stream.IsStreamOwner = false;
                byte[] input = source.ToArray();
                stream.Write(input, 0, input.Length);
                stream.Finish();
            }
#else
            using (ZLibStream stream = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                stream.Write(source);
            }
#endif
            return output.ToArray();
        }

        public static ExrResult Inflate(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            // ZLibStream can report EOF for a truncated trailer. Check the RFC 1950
            // wrapper explicitly so both framework backends reject the same damage.
            if (source.Length < 6 || (source[0] & 15) != 8 || (source[0] >> 4) > 7 ||
                ((source[0] << 8) + source[1]) % 31 != 0 || (source[1] & 32) != 0)
                return ExrResult.Corrupt;
            try
            {
                using MemoryStream input = new MemoryStream(source.ToArray(), writable: false);
#if NETSTANDARD2_1
                using InflaterInputStream stream = new InflaterInputStream(input, new Inflater());
#else
                using ZLibStream stream = new ZLibStream(input, CompressionMode.Decompress);
#endif
                int offset = 0;
                while (offset < destination.Length)
                {
                    int read = stream.Read(destination.Slice(offset));
                    if (read == 0)
                        return ExrResult.Corrupt;
                    offset += read;
                }
                if (stream.ReadByte() != -1)
                    return ExrResult.Corrupt;
                uint a = 1, b = 0;
                // Reduce at the Adler-32 maximum safe interval for uint accumulators.
                for (int start = 0; start < destination.Length; start += 5552)
                {
                    int end = Math.Min(destination.Length, start + 5552);
                    for (int i = start; i < end; i++)
                    {
                        a += destination[i];
                        b += a;
                    }
                    a %= 65521;
                    b %= 65521;
                }
                return ((b << 16) | a) == BinaryPrimitives.ReadUInt32BigEndian(source.Slice(source.Length - 4))
                    ? ExrResult.Success : ExrResult.Corrupt;
            }
            catch (InvalidDataException)
            {
                return ExrResult.Corrupt;
            }
            catch (IOException)
            {
                return ExrResult.Corrupt;
            }
#if NETSTANDARD2_1
            catch (ICSharpCode.SharpZipLib.SharpZipBaseException)
            {
                return ExrResult.Corrupt;
            }
#endif
        }
    }
}
