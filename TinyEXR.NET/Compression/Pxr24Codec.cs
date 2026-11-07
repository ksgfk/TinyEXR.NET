// Port of tinyexr v3 src/exr_pxr24.c, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
using System.Buffers.Binary;

namespace TinyEXR.Codecs
{
    internal static class Pxr24Codec
    {
        private static int PlaneCount(PixelType type) => type == PixelType.Float ? 3 : ModelValidation.PixelTypeSize(type);

        private static int IntermediateSize(CodecContext context)
        {
            int count = 0;
            for (int c = 0; c < context.Channels.Count; c++)
                count = checked(count + context.Width(c) * context.Height(c) * PlaneCount(context.Channels[c].PixelType));
            return count;
        }

        public static ExrResult Decode(CodecContext context, ReadOnlySpan<byte> source, Span<byte> destination, CodecWorkspace workspace)
        {
            int length = IntermediateSize(context);
            Span<byte> planes = workspace.Bytes(0, length).AsSpan(0, length);
            // Only the full canonical byte count denotes raw fallback. A zlib
            // payload may coincidentally equal the reduced 24-bit plane size.
            ExrResult result = ZlibBackend.Inflate(source, planes);
            if (result != ExrResult.Success) return result;
            int input = 0, output = 0;
            for (long y = context.Region.MinY; y <= context.Region.MaxY; y++)
            {
                for (int c = 0; c < context.Channels.Count; c++)
                {
                    Channel channel = context.Channels[c];
                    if (y % channel.YSampling != 0)
                        continue;
                    int width = context.Width(c), count = PlaneCount(channel.PixelType), size = context.SampleSize(c);
                    uint pixel = 0;
                    for (int x = 0; x < width; x++)
                    {
                        uint delta = 0;
                        for (int p = 0; p < count; p++)
                            delta = (delta << 8) | planes[input + p * width + x];
                        if (channel.PixelType == PixelType.Float)
                            delta <<= 8;
                        pixel = unchecked(pixel + delta);
                        if (size > destination.Length - output)
                            return ExrResult.Corrupt;
                        if (size == 2)
                            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(output), (ushort)pixel);
                        else
                            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(output), pixel);
                        output += size;
                    }
                    input += checked(width * count);
                }
            }
            return output == destination.Length ? ExrResult.Success : ExrResult.Corrupt;
        }

        public static byte[] Encode(CodecContext context, ReadOnlySpan<byte> source, CodecWorkspace workspace)
        {
            int length = IntermediateSize(context);
            Span<byte> planes = workspace.Bytes(0, length).AsSpan(0, length);
            int input = 0, output = 0;
            for (long y = context.Region.MinY; y <= context.Region.MaxY; y++)
            {
                for (int c = 0; c < context.Channels.Count; c++)
                {
                    Channel channel = context.Channels[c];
                    if (y % channel.YSampling != 0)
                        continue;
                    int width = context.Width(c), count = PlaneCount(channel.PixelType), size = context.SampleSize(c);
                    uint previous = 0;
                    for (int x = 0; x < width; x++)
                    {
                        uint pixel = size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(input)) : BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(input));
                        if (channel.PixelType == PixelType.Float)
                            pixel = Float24(pixel);
                        uint delta = unchecked(pixel - previous);
                        previous = pixel;
                        for (int p = count - 1; p >= 0; p--)
                        {
                            planes[output + p * width + x] = (byte)delta;
                            delta >>= 8;
                        }
                        input += size;
                    }
                    output += checked(width * count);
                }
            }
            return ZlibBackend.Deflate(planes);
        }

        private static uint Float24(uint bits)
        {
            uint sign = bits & 0x80000000U, exponent = bits & 0x7f800000U, mantissa = bits & 0x007fffffU;
            if (exponent == 0x7f800000U)
            {
                if (mantissa == 0)
                    return (sign | exponent) >> 8;
                mantissa >>= 8;
                return (sign >> 8) | (exponent >> 8) | mantissa | (mantissa == 0 ? 1U : 0U);
            }
            uint rounded = ((exponent | mantissa) + (mantissa & 0x80U)) >> 8;
            if (rounded >= 0x7f8000U)
                rounded = (exponent | mantissa) >> 8;
            return (sign >> 8) | rounded;
        }
    }
}
