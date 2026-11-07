// Port of tinyexr v3 exr_piz.c wav2_encode/wav2_decode, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    internal static class PizWavelet
    {
        public static void Transform(Span<ushort> data, int origin, int width, int height, int lanes, bool small, bool encode)
        {
            int minimum = Math.Min(width, height);
            if (minimum < 2)
                return;
            int step = 1;
            if (!encode)
            {
                while (step <= minimum / 2)
                    step <<= 1;
                step >>= 1;
            }
            while (step >= 1 && step <= minimum / 2)
            {
                int doubleStep = step * 2;
                int y = 0;
                for (; y <= height - doubleStep; y += doubleStep)
                {
                    int x = 0;
                    for (; x <= width - doubleStep; x += doubleStep)
                    {
                        int a = origin + (y * width + x) * lanes;
                        int b = a + step * lanes;
                        int c = a + step * width * lanes;
                        int d = c + step * lanes;
                        if (encode)
                        {
                            Pair(data[a], data[b], out ushort i00, out ushort i01, small, true);
                            Pair(data[c], data[d], out ushort i10, out ushort i11, small, true);
                            Pair(i00, i10, out data[a], out data[c], small, true);
                            Pair(i01, i11, out data[b], out data[d], small, true);
                        }
                        else
                        {
                            Pair(data[a], data[c], out ushort i00, out ushort i10, small, false);
                            Pair(data[b], data[d], out ushort i01, out ushort i11, small, false);
                            Pair(i00, i01, out data[a], out data[b], small, false);
                            Pair(i10, i11, out data[c], out data[d], small, false);
                        }
                    }
                    if ((width & step) != 0)
                    {
                        int a = origin + (y * width + x) * lanes;
                        int b = a + step * width * lanes;
                        Pair(data[a], data[b], out data[a], out data[b], small, encode);
                    }
                }
                if ((height & step) != 0)
                {
                    for (int x = 0; x <= width - doubleStep; x += doubleStep)
                    {
                        int a = origin + (y * width + x) * lanes;
                        int b = a + step * lanes;
                        Pair(data[a], data[b], out data[a], out data[b], small, encode);
                    }
                }
                step = encode ? doubleStep : step / 2;
            }
        }

        private static void Pair(ushort first, ushort second, out ushort a, out ushort b, bool small, bool encode)
        {
            unchecked
            {
                if (small)
                {
                    int x = (short)first, y = (short)second;
                    if (encode)
                    {
                        a = (ushort)((x + y) >> 1);
                        b = (ushort)(x - y);
                    }
                    else
                    {
                        int value = x + (y & 1) + (y >> 1);
                        a = (ushort)value;
                        b = (ushort)(value - y);
                    }
                }
                else if (encode)
                {
                    int shifted = (first + 32768) & 65535;
                    int mean = (shifted + second) >> 1, difference = shifted - second;
                    if (difference < 0)
                        mean = (mean + 32768) & 65535;
                    a = (ushort)mean;
                    b = (ushort)difference;
                }
                else
                {
                    int value = (first - (second >> 1)) & 65535;
                    a = (ushort)(second + value - 32768);
                    b = (ushort)value;
                }
            }
        }
    }
}
