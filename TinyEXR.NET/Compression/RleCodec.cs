// Port of tinyexr v3 src/exr_rle.c, revision eb0ba07f44a33e1ce5bc0fb74142a3a6558f0661.
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
            if (output != expanded.Length || input != source.Length)
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
            int capacity = checked((int)(source.Length + ((long)source.Length + 126) / 127 + 1));
            Span<byte> output = workspace.Bytes(3, capacity).AsSpan(0, capacity);
            int start = 0, written = 0;
            while (start < predicted.Length)
            {
                int end = start + 1;
                while (end < predicted.Length && predicted[start] == predicted[end] && end - start - 1 < 127)
                    end++;
                if (end - start >= 3)
                {
                    output[written++] = (byte)(end - start - 1);
                    output[written++] = predicted[start];
                }
                else
                {
                    end = start;
                    while (end < predicted.Length && end - start < 127)
                    {
                        if (end + 2 < predicted.Length && predicted[end] == predicted[end + 1] && predicted[end + 1] == predicted[end + 2])
                            break;
                        end++;
                    }
                    output[written++] = unchecked((byte)(start - end));
                    predicted.Slice(start, end - start).CopyTo(output.Slice(written));
                    written += end - start;
                }
                start = end;
            }
            return output.Slice(0, written).ToArray();
        }
    }
}
