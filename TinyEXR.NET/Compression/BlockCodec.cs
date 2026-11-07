// Port of tinyexr v3 exr_codec.c dispatch/raw fallback, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    internal static class BlockCodec
    {
        public static ExrResult Decode(Compression compression, CodecContext context, ReadOnlySpan<byte> source,
            Span<byte> destination, CodecWorkspace workspace)
        {
            try
            {
                if (context.ByteCount != destination.Length)
                    return ExrResult.Corrupt;
                if (source.Length == destination.Length)
                {
                    source.CopyTo(destination);
                    return ExrResult.Success;
                }
                if (source.Length > destination.Length)
                    return ExrResult.Corrupt;
                return compression switch
                {
                    Compression.None => ExrResult.Corrupt,
                    Compression.RLE => RleCodec.Decode(source, destination, workspace),
                    Compression.ZIP or Compression.ZIPS => ZipCodec.Decode(source, destination, workspace),
                    Compression.PIZ => PizCodec.Decode(context, source, destination, workspace),
                    Compression.PXR24 => Pxr24Codec.Decode(context, source, destination, workspace),
                    Compression.B44 or Compression.B44A => B44Codec.Decode(context, source, destination, workspace),
                    _ => ExrResult.Unsupported,
                };
            }
            catch (OutOfMemoryException)
            {
                return ExrResult.OutOfMemory;
            }
            catch (OverflowException)
            {
                return ExrResult.Corrupt;
            }
            catch (ArgumentException)
            {
                return ExrResult.Corrupt;
            }
            catch (IndexOutOfRangeException)
            {
                return ExrResult.Corrupt;
            }
        }

        public static ExrResult Encode(Compression compression, CodecContext context, ReadOnlySpan<byte> source,
            CodecWorkspace workspace, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            try
            {
                if (context.ByteCount != source.Length)
                    return ExrResult.InvalidArgument;
                switch (compression)
                {
                    case Compression.None:
                        payload = source.ToArray();
                        break;
                    case Compression.RLE:
                        payload = RleCodec.Encode(source, workspace);
                        break;
                    case Compression.ZIP:
                    case Compression.ZIPS:
                        payload = ZipCodec.Encode(source, workspace);
                        break;
                    case Compression.PIZ:
                        payload = PizCodec.Encode(context, source, workspace);
                        break;
                    case Compression.PXR24:
                        payload = Pxr24Codec.Encode(context, source, workspace);
                        break;
                    case Compression.B44:
                    case Compression.B44A:
                        payload = B44Codec.Encode(context, source, workspace, compression == Compression.B44A);
                        break;
                    default:
                        return ExrResult.Unsupported;
                }
                if (payload.Length >= source.Length)
                    payload = source.ToArray();
                return ExrResult.Success;
            }
            catch (OutOfMemoryException)
            {
                payload = Array.Empty<byte>();
                return ExrResult.OutOfMemory;
            }
            catch (OverflowException)
            {
                payload = Array.Empty<byte>();
                return ExrResult.Unsupported;
            }
            catch (ArgumentException)
            {
                payload = Array.Empty<byte>();
                return ExrResult.InvalidArgument;
            }
        }

        // Deep count and sample payloads share the same byte transform, independently of channel layout.
        public static ExrResult DecodeDeep(Compression compression, ReadOnlySpan<byte> source, Span<byte> destination, CodecWorkspace workspace)
        {
            if (source.Length == destination.Length)
            {
                source.CopyTo(destination);
                return ExrResult.Success;
            }
            if (source.Length > destination.Length)
                return ExrResult.Corrupt;
            return compression switch
            {
                Compression.RLE => RleCodec.Decode(source, destination, workspace),
                Compression.ZIP or Compression.ZIPS => ZipCodec.Decode(source, destination, workspace),
                Compression.None or Compression.HTJ2K32 or Compression.HTJ2K256 => ExrResult.Corrupt,
                _ => ExrResult.Unsupported,
            };
        }

        public static byte[] EncodeDeep(Compression compression, ReadOnlySpan<byte> source, CodecWorkspace workspace)
        {
            byte[] payload = compression switch
            {
                Compression.RLE => RleCodec.Encode(source, workspace),
                Compression.ZIP or Compression.ZIPS => ZipCodec.Encode(source, workspace),
                Compression.None or Compression.HTJ2K32 or Compression.HTJ2K256 => source.ToArray(),
                _ => throw new NotSupportedException($"Compression '{compression}' cannot encode deep payloads."),
            };
            return payload.Length < source.Length ? payload : source.ToArray();
        }
    }
}
