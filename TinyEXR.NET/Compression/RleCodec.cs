// Port of tinyexr v3 src/exr_rle.c, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    internal static class RleCodec
    {
        public static ExrResult Decode(ReadOnlySpan<byte> source, Span<byte> destination, CodecWorkspace workspace)
        {
            Span<byte> expanded = workspace.Bytes(0, destination.Length).AsSpan(0, destination.Length);
            int input = 0, output = 0;
            while (input < source.Length && output < expanded.Length)
            {
                int count = unchecked((sbyte)source[input++]);
                int length = count < 0 ? -count : count + 1;
                if (length > expanded.Length - output)
                    return ExrResult.Corrupt;
                if (count < 0)
                {
                    if (length > source.Length - input)
                        return ExrResult.Corrupt;
                    source.Slice(input, length).CopyTo(expanded.Slice(output, length));
                    input += length;
                }
                else
                {
                    if (input == source.Length)
                        return ExrResult.Corrupt;
                    expanded.Slice(output, length).Fill(source[input++]);
                }
                output += length;
            }
            if (output != expanded.Length)
                return ExrResult.Corrupt;
            ExrPredictor.Undo(expanded, expanded.Length, destination);
            return ExrResult.Success;
        }

        public static byte[] Encode(ReadOnlySpan<byte> source, CodecWorkspace workspace)
        {
            if (source.IsEmpty)
                return Array.Empty<byte>();
            Span<byte> predicted = workspace.Bytes(0, source.Length).AsSpan(0, source.Length);
            ExrPredictor.Apply(source, predicted);
            using MemoryStream output = new MemoryStream();
            int start = 0, end = 1;
            while (start < predicted.Length)
            {
                while (end < predicted.Length && predicted[start] == predicted[end] && end - start - 1 < 127)
                    end++;
                if (end - start >= 3)
                {
                    output.WriteByte((byte)(end - start - 1));
                    output.WriteByte(predicted[start]);
                    start = end;
                }
                else
                {
                    while (end < predicted.Length &&
                        ((end + 1 >= predicted.Length || predicted[end] != predicted[end + 1]) ||
                         (end + 2 >= predicted.Length || predicted[end + 1] != predicted[end + 2])) && end - start < 127)
                        end++;
                    output.WriteByte(unchecked((byte)(start - end)));
                    output.Write(predicted.Slice(start, end - start));
                    start = end;
                }
                end++;
            }
            return output.ToArray();
        }
    }
}
