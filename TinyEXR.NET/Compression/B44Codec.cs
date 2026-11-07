// Port of tinyexr v3 src/exr_b44.c, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
using System.Buffers.Binary;

namespace TinyEXR.Codecs
{
    internal static class B44Codec
    {
        private static readonly int[] Previous = { 0, 4, 8, 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14 };
        private static readonly int[] Next = { 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15 };

        public static ExrResult Decode(CodecContext context, ReadOnlySpan<byte> source, Span<byte> destination, CodecWorkspace workspace)
        {
            Span<byte> planes = workspace.Bytes(2, destination.Length).AsSpan(0, destination.Length);
            Span<ushort> block = stackalloc ushort[16];
            int input = 0, plane = 0;
            for (int c = 0; c < context.Channels.Count; c++)
            {
                Channel channel = context.Channels[c];
                int width = context.Width(c), height = context.Height(c), bytes = checked(width * height * context.SampleSize(c));
                if (channel.PixelType != PixelType.Half)
                {
                    if (bytes > source.Length - input)
                        return ExrResult.Corrupt;
                    source.Slice(input, bytes).CopyTo(planes.Slice(plane, bytes));
                    input += bytes;
                }
                else
                {
                    for (int y = 0; y < height; y += 4)
                    {
                        for (int x = 0; x < width; x += 4)
                        {
                            if (source.Length - input < 3)
                                return ExrResult.Corrupt;
                            int length = source[input + 2] >= 52 ? 3 : 14;
                            if (length > source.Length - input)
                                return ExrResult.Corrupt;
                            Unpack(source.Slice(input, length), block);
                            input += length;
                            for (int dy = 0; dy < 4 && y + dy < height; dy++)
                            {
                                for (int dx = 0; dx < 4 && x + dx < width; dx++)
                                {
                                    ushort value = block[dy * 4 + dx];
                                    if (channel.PerceptuallyLinear)
                                        value = B44Tables.Log[value];
                                    BinaryPrimitives.WriteUInt16LittleEndian(planes.Slice(plane + ((y + dy) * width + x + dx) * 2), value);
                                }
                            }
                        }
                    }
                }
                plane += bytes;
            }
            context.Reorder(planes, destination, false);
            return ExrResult.Success;
        }

        public static byte[] Encode(CodecContext context, ReadOnlySpan<byte> source, CodecWorkspace workspace, bool optimizeFlat)
        {
            Span<byte> planes = workspace.Bytes(2, source.Length).AsSpan(0, source.Length);
            context.Reorder(source, planes, true);
            using MemoryStream output = new MemoryStream();
            Span<ushort> block = stackalloc ushort[16];
            Span<byte> packed = stackalloc byte[14];
            int plane = 0;
            for (int c = 0; c < context.Channels.Count; c++)
            {
                Channel channel = context.Channels[c];
                int width = context.Width(c), height = context.Height(c), bytes = checked(width * height * context.SampleSize(c));
                if (channel.PixelType != PixelType.Half)
                    output.Write(planes.Slice(plane, bytes));
                else
                {
                    for (int y = 0; y < height; y += 4)
                    {
                        for (int x = 0; x < width; x += 4)
                        {
                            for (int dy = 0; dy < 4; dy++)
                            {
                                for (int dx = 0; dx < 4; dx++)
                                {
                                    int offset = plane + (Math.Min(y + dy, height - 1) * width + Math.Min(x + dx, width - 1)) * 2;
                                    ushort value = BinaryPrimitives.ReadUInt16LittleEndian(planes.Slice(offset));
                                    if (channel.PerceptuallyLinear)
                                        value = B44Tables.Exp[value];
                                    block[dy * 4 + dx] = value;
                                }
                            }
                            int length = Pack(block, packed, optimizeFlat, !channel.PerceptuallyLinear);
                            output.Write(packed.Slice(0, length));
                        }
                    }
                }
                plane += bytes;
            }
            return output.ToArray();
        }

        private static void Unpack(ReadOnlySpan<byte> source, Span<ushort> block)
        {
            block[0] = BinaryPrimitives.ReadUInt16BigEndian(source);
            if (source.Length == 3)
                block.Fill(block[0]);
            else
            {
                int shift = source[2] >> 2, bit = 22;
                for (int i = 0; i < 15; i++)
                {
                    int byteIndex = bit >> 3, remaining = 8 - (bit & 7);
                    int six = source[byteIndex] & ((1 << remaining) - 1);
                    if (remaining < 6)
                        six = (six << (6 - remaining)) | (source[byteIndex + 1] >> (8 - (6 - remaining)));
                    else
                        six >>= remaining - 6;
                    block[Next[i]] = unchecked((ushort)(block[Previous[i]] + ((six - 32) << shift)));
                    bit += 6;
                }
            }
            for (int i = 0; i < block.Length; i++)
                block[i] = (block[i] & 0x8000) != 0 ? (ushort)(block[i] & 0x7fff) : unchecked((ushort)~block[i]);
        }

        private static int Pack(ReadOnlySpan<ushort> block, Span<byte> output, bool optimizeFlat, bool exactMaximum)
        {
            Span<ushort> transformed = stackalloc ushort[16];
            Span<int> distances = stackalloc int[16];
            Span<int> deltas = stackalloc int[15];
            int maximum = 0;
            for (int i = 0; i < block.Length; i++)
            {
                ushort value = block[i];
                transformed[i] = (value & 0x7c00) == 0x7c00 ? (ushort)0x8000 : (value & 0x8000) != 0 ? unchecked((ushort)~value) : (ushort)(value | 0x8000);
                maximum = Math.Max(maximum, transformed[i]);
            }
            int shift = -1, minimumDelta, maximumDelta;
            do
            {
                shift++;
                for (int i = 0; i < distances.Length; i++)
                {
                    int x = (maximum - transformed[i]) << 1;
                    distances[i] = (x + ((1 << shift) - 1) + ((x >> (shift + 1)) & 1)) >> (shift + 1);
                }
                minimumDelta = int.MaxValue;
                maximumDelta = int.MinValue;
                for (int i = 0; i < deltas.Length; i++)
                {
                    deltas[i] = distances[Previous[i]] - distances[Next[i]] + 32;
                    minimumDelta = Math.Min(minimumDelta, deltas[i]);
                    maximumDelta = Math.Max(maximumDelta, deltas[i]);
                }
            } while (minimumDelta < 0 || maximumDelta > 63);
            if (minimumDelta == 32 && maximumDelta == 32 && optimizeFlat)
            {
                BinaryPrimitives.WriteUInt16BigEndian(output, transformed[0]);
                output[2] = 252;
                return 3;
            }
            output.Clear();
            BinaryPrimitives.WriteUInt16BigEndian(output, exactMaximum ? unchecked((ushort)(maximum - (distances[0] << shift))) : transformed[0]);
            output[2] = (byte)(shift << 2);
            int bit = 22;
            for (int i = 0; i < deltas.Length; i++)
            {
                for (int b = 5; b >= 0; b--, bit++)
                    output[bit >> 3] |= (byte)(((deltas[i] >> b) & 1) << (7 - (bit & 7)));
            }
            return 14;
        }
    }
}
