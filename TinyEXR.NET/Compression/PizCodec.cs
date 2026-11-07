// Port of tinyexr v3 src/exr_piz.c, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
using System.Buffers.Binary;

namespace TinyEXR.Codecs
{
    internal static class PizCodec
    {
        public static ExrResult Decode(CodecContext context, ReadOnlySpan<byte> source, Span<byte> destination, CodecWorkspace workspace)
        {
            if (context.Channels.Count > 1024 || source.Length < 4 || destination.Length == 0 || (destination.Length & 1) != 0)
                return ExrResult.Corrupt;
            int minimum = BinaryPrimitives.ReadUInt16LittleEndian(source), maximum = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(2));
            if (maximum >= 8192 || (minimum > maximum && (minimum != 8191 || maximum != 0)))
                return ExrResult.Corrupt;
            Span<byte> bitmap = workspace.Bytes(1, 8192).AsSpan(0, 8192);
            bitmap.Clear();
            int position = 4;
            if (minimum <= maximum)
            {
                int length = maximum - minimum + 1;
                if (length > source.Length - position)
                    return ExrResult.Corrupt;
                source.Slice(position, length).CopyTo(bitmap.Slice(minimum));
                position += length;
            }
            if (source.Length - position < 4)
                return ExrResult.Corrupt;
            uint huffmanLength = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(position));
            position += 4;
            if (huffmanLength > source.Length - position)
                return ExrResult.Corrupt;
            Span<ushort> lut = workspace.Words(1, 65536).AsSpan(0, 65536);
            lut.Clear();
            int values = 0;
            for (int i = 0; i < 65536; i++)
            if (i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0)
                lut[values++] = (ushort)i;
            Span<ushort> words = workspace.Words(0, destination.Length / 2).AsSpan(0, destination.Length / 2);
            if (!PizHuffman.Decode(source.Slice(position, (int)huffmanLength), words, workspace.Huffman))
                return ExrResult.Corrupt;
            Transform(context, words, values - 1, false);
            Span<byte> planes = workspace.Bytes(2, destination.Length).AsSpan(0, destination.Length);
            for (int i = 0; i < words.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(planes.Slice(i * 2), lut[words[i]]);
            context.Reorder(planes, destination, false);
            return ExrResult.Success;
        }

        public static byte[] Encode(CodecContext context, ReadOnlySpan<byte> source, CodecWorkspace workspace)
        {
            if (context.Channels.Count > 1024 || (source.Length & 1) != 0)
                throw new ArgumentException("Invalid PIZ channel layout.");
            if (source.IsEmpty)
                return Array.Empty<byte>();
            Span<byte> planes = workspace.Bytes(2, source.Length).AsSpan(0, source.Length);
            context.Reorder(source, planes, true);
            Span<ushort> words = workspace.Words(0, source.Length / 2).AsSpan(0, source.Length / 2);
            Span<byte> bitmap = workspace.Bytes(1, 8192).AsSpan(0, 8192);
            bitmap.Clear();
            for (int i = 0; i < words.Length; i++)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(planes.Slice(i * 2));
                words[i] = value;
                bitmap[value >> 3] |= (byte)(1 << (value & 7));
            }
            bitmap[0] &= 254;
            int minimum = 8191, maximum = 0;
            for (int i = 0; i < bitmap.Length; i++)
            if (bitmap[i] != 0)
            {
                minimum = Math.Min(minimum, i);
                maximum = i;
            }
            Span<ushort> lut = workspace.Words(1, 65536).AsSpan(0, 65536);
            int values = 0;
            for (int i = 0; i < lut.Length; i++)
                lut[i] = i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0 ? (ushort)values++ : (ushort)0;
            for (int i = 0; i < words.Length; i++)
                words[i] = lut[words[i]];
            Transform(context, words, values - 1, true);
            byte[] huffman = PizHuffman.Encode(words, workspace.Huffman);
            int bitmapLength = Math.Max(0, maximum - minimum + 1);
            byte[] output = new byte[checked(8 + bitmapLength + huffman.Length)];
            BinaryPrimitives.WriteUInt16LittleEndian(output, (ushort)minimum);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2), (ushort)maximum);
            if (bitmapLength > 0)
                bitmap.Slice(minimum, bitmapLength).CopyTo(output.AsSpan(4));
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4 + bitmapLength), huffman.Length);
            huffman.CopyTo(output, 8 + bitmapLength);
            return output;
        }

        private static void Transform(CodecContext context, Span<ushort> words, int maximum, bool encode)
        {
            int start = 0;
            for (int c = 0; c < context.Channels.Count; c++)
            {
                int width = context.Width(c), height = context.Height(c), lanes = context.SampleSize(c) / 2;
                for (int lane = 0; lane < lanes; lane++)
                    PizWavelet.Transform(words, start + lane, width, height, lanes, maximum < 16384, encode);
                start += checked(width * height * lanes);
            }
        }
    }
}
