// Port of tinyexr v3 exr_b44.c perceptual tables, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    // CLR type initialization publishes these immutable tables atomically.
    internal static class B44Tables
    {
        internal static readonly ushort[] Exp = new ushort[65536];
        internal static readonly ushort[] Log = new ushort[65536];

        static B44Tables()
        {
            for (int i = 0; i < 65536; i++)
            {
                ushort half = (ushort)i;
                if ((half & 0x7c00) == 0x7c00)
                    continue;
                float value = PixelConversion.HalfBitsToSingle(half);
                Exp[i] = half >= 0x558c && half < 0x8000 ? (ushort)0x7bff : TruncateHalf((float)ExpValue(value / 8.0));
                Log[i] = value <= 0 ? (ushort)0 : TruncateHalf((float)(8.0 * LogValue(value)));
            }
        }

        private static ushort TruncateHalf(float value)
        {
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            int sign = (int)(bits >> 31), exponent = (int)((bits >> 23) & 255), mantissa = (int)(bits & 0x7fffff);
            if (exponent == 0)
                return (ushort)(sign << 15);
            if (exponent == 255)
                return (ushort)((sign << 15) | 0x7c00 | (mantissa >> 13));
            if (exponent < 113)
            {
                if (exponent < 103)
                    return (ushort)(sign << 15);
                mantissa = (mantissa | 0x800000) >> (114 - exponent);
                return (ushort)((sign << 15) | (mantissa >> 13));
            }
            if (exponent > 142)
                return (ushort)((sign << 15) | 0x7c00);
            return (ushort)((sign << 15) | ((exponent - 112) << 10) | (mantissa >> 13));
        }

        private static double Scale2(int exponent)
        {
            exponent = Math.Min(exponent, 1023);
            if (exponent < -1074)
                return 0;
            if (exponent >= -1022)
                return BitConverter.Int64BitsToDouble((long)(exponent + 1023) << 52);
            return BitConverter.Int64BitsToDouble((long)(exponent + 54 + 1023) << 52) * (1.0 / 18014398509481984.0);
        }

        private static double ExpValue(double value)
        {
            int exponent = (int)(value * 1.4426950408889634 + (value < 0 ? -0.5 : 0.5));
            double r = (value - exponent * 6.93145751953125e-1) - exponent * 1.42860682030941723212e-6;
            double polynomial = r + r * r * (0.5 + r * (1.0 / 6 + r * (1.0 / 24 + r * (1.0 / 120 +
                r * (1.0 / 720 + r * (1.0 / 5040 + r * (1.0 / 40320 + r * (1.0 / 362880))))))));
            return (1.0 + polynomial) * Scale2(exponent);
        }

        private static double LogValue(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = 0;
            if (((bits >> 52) & 2047) == 0)
            {
                bits = BitConverter.DoubleToInt64Bits(value * 18014398509481984.0);
                exponent = -54;
            }
            exponent += (int)((bits >> 52) & 2047) - 1023;
            double mantissa = BitConverter.Int64BitsToDouble((bits & 0x000fffffffffffffL) | 0x3ff0000000000000L);
            if (mantissa > 1.4142135623730951)
            {
                mantissa *= 0.5;
                exponent++;
            }
            double f = (mantissa - 1) / (mantissa + 1), f2 = f * f;
            double sum = f * (2 + f2 * (2.0 / 3 + f2 * (2.0 / 5 + f2 * (2.0 / 7 + f2 * (2.0 / 9 +
                f2 * (2.0 / 11 + f2 * (2.0 / 13)))))));
            return exponent * 0.6931471805599453 + sum;
        }
    }
}
