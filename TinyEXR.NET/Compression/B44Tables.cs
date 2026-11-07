// OpenEXR 3.5.3 internal_b44_table_init.c, commit 3950290530c4fd4776667d0dc4d65a9d003eb936.
// Copyright Contributors to the OpenEXR Project. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    internal static class B44Tables
    {
        // CLR initialization publishes the complete tables atomically.
        internal static readonly ushort[] Exp = new ushort[65536];
        internal static readonly ushort[] Log = new ushort[65536];

        static B44Tables()
        {
            for (int i = 0; i < 65536; i++)
            {
                ushort half = (ushort)i;
                if ((half & 0x7c00) == 0x7c00) continue;
                float value = PixelConversion.HalfBitsToSingle(half);
                Exp[i] = half >= 0x558c && half < 0x8000 ? (ushort)0x7bff :
                    PixelConversion.SingleToHalfBits(MathF.Exp(value / 8f));
                Log[i] = half > 0x8000 ? (ushort)0 :
                    PixelConversion.SingleToHalfBits(8f * MathF.Log(value));
            }
        }
    }
}
