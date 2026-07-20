using System;

namespace TinyEXR.V3.Codecs
{
    internal static partial class Htj2kDecoder
    {
        private static readonly HtTables Tables = HtTables.Create();

        private static long[] DecodeCodeBlock(byte[] source, CodeBlock codeBlock, uint kmax)
        {
            uint width = codeBlock.Width;
            uint height = codeBlock.Height;
            uint missingMsbs = codeBlock.MissingMsbs;
            uint passCount = codeBlock.ActivePasses;
            uint cleanupLength = codeBlock.Length0;
            uint refinementLength = codeBlock.Length1;

            Require(width >= 1 && width <= 128 && height >= 1 && height <= 32,
                "An HT codeblock has invalid dimensions.");
            if (passCount > 1 && refinementLength == 0)
            {
                passCount = 1;
            }

            Support(passCount >= 1 && passCount <= 3, "An HT codeblock has more than three coding passes.");
            Require(missingMsbs <= 62 && cleanupLength >= 2, "An HT codeblock has invalid bitplane metadata.");

            CodeBlockStreams streams = SplitCodeBlockStreams(source, codeBlock);
            uint p = 62 - missingMsbs;
            Support(p != 0, "An HT codeblock uses an unsupported 64-bit precision plane.");
            uint missingMsbsPlusTwo = missingMsbs + 2;
            int scratchStride = checked((int)((width + 9) & ~7u));
            int scratchCount = checked(scratchStride * (int)(((height + 1) >> 1) + 1));
            int bufferCount = checked(scratchStride * (int)Math.Max(height, 4));

            ushort[] scratch = new ushort[scratchCount];
            ulong[] north = new ulong[516];
            ulong[] buffer = new ulong[bufferCount];
            MelReader mel = new MelReader(source, streams.MelOffset, streams.MelLength);
            VlcReverseReader vlc = VlcReverseReader.CreateCleanup(
                source,
                codeBlock.DataOffset,
                checked((int)cleanupLength),
                streams.Scup);
            ForwardBitReader magSgn = new ForwardBitReader(
                source,
                streams.MagSgnOffset,
                streams.MagSgnLength,
                fillWithOnes: true);

            mel.GetRun(out uint run, out bool hasOne);
            run = hasOne ? (run << 1) | 1u : run << 1;
            DecodeCleanupVlc(width, height, scratchStride, scratch, mel, vlc, ref run);
            DecodeCleanupMagnitudes(
                width,
                height,
                p,
                missingMsbsPlusTwo,
                scratchStride,
                scratch,
                north,
                buffer,
                magSgn);

            if (passCount > 1)
            {
                DecodeSignificancePropagation(
                    source,
                    codeBlock,
                    p,
                    scratchStride,
                    scratchCount,
                    scratch,
                    buffer);
                if (passCount > 2)
                {
                    DecodeMagnitudeRefinement(
                        source,
                        codeBlock,
                        p,
                        scratchStride,
                        scratch,
                        buffer);
                }
            }

            long[] result = new long[checked((int)(width * height))];
            uint shift = kmax < 63 ? 63 - kmax : 0;
            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    ulong value = buffer[checked((int)(y * (uint)scratchStride + x))];
                    ulong magnitude = (value & 0x7fffffffffffffffUL) >> (int)shift;
                    result[checked((int)(y * width + x))] =
                        (value & 0x8000000000000000UL) != 0
                            ? -(long)magnitude
                            : (long)magnitude;
                }
            }

            return result;
        }

        private static void DecodeCleanupVlc(
            uint width,
            uint height,
            int stride,
            ushort[] scratch,
            MelReader mel,
            VlcReverseReader vlc,
            ref uint run)
        {
            uint context = 0;
            int scratchOffset = 0;
            uint x = 0;
            while (x < width)
            {
                ulong value = vlc.Fetch64();
                ushort first = Tables.Vlc0[context + ((uint)value & 0x7f)];
                if (context == 0)
                {
                    run -= 2;
                    first = run == uint.MaxValue ? first : (ushort)0;
                    if ((int)run < 0)
                    {
                        mel.GetRun(out run, out bool hasOne);
                        run = hasOne ? (run << 1) | 1u : run << 1;
                    }
                }

                scratch[scratchOffset] = first;
                x += 2;
                context = ((uint)(first & 0x10) << 3) | ((uint)(first & 0xe0) << 2);
                value = vlc.Advance64((uint)first & 7);

                ushort second = Tables.Vlc0[context + ((uint)value & 0x7f)];
                if (context == 0 && x < width)
                {
                    run -= 2;
                    second = run == uint.MaxValue ? second : (ushort)0;
                    if ((int)run < 0)
                    {
                        mel.GetRun(out run, out bool hasOne);
                        run = hasOne ? (run << 1) | 1u : run << 1;
                    }
                }

                second = x < width ? second : (ushort)0;
                scratch[scratchOffset + 2] = second;
                x += 2;
                context = ((uint)(second & 0x10) << 3) | ((uint)(second & 0xe0) << 2);
                value = vlc.Advance64((uint)second & 7);

                uint mode = ((uint)(first & 8) << 3) | ((uint)(second & 8) << 4);
                if (mode == 0xc0)
                {
                    run -= 2;
                    mode += run == uint.MaxValue ? 0x40u : 0;
                    if ((int)run < 0)
                    {
                        mel.GetRun(out run, out bool hasOne);
                        run = hasOne ? (run << 1) | 1u : run << 1;
                    }
                }

                uint rawIndex = mode + ((uint)value & 0x3f);
                Require(rawIndex < Tables.Uvlc0.Length, "An HT cleanup UVLC0 index is invalid.");
                int index = (int)rawIndex;
                ushort entry = Tables.Uvlc0[index];
                uint bias = Tables.UvlcBias[index];
                value = vlc.Advance64((uint)entry & 7);
                entry >>= 3;
                int suffixLength = entry & 0x0f;
                uint suffix = (uint)value & Mask32(suffixLength);
                value = vlc.Advance64((uint)suffixLength);
                entry >>= 4;
                int firstSuffixLength = entry & 7;
                entry >>= 3;
                uint q0 = ((uint)entry & 7) + (suffix & Mask32(firstSuffixLength));
                uint q1 = ((uint)entry >> 3) + (suffix >> firstSuffixLength);

                bool extended = q0 - (bias & 3) > 32;
                uint extension = extended ? (uint)value & 0x0f : 0;
                value = vlc.Advance64(extended ? 4u : 0);
                q0 += extension << 2;
                Require(q0 < ushort.MaxValue, "An HT cleanup UVLC0 q0 value is invalid.");
                scratch[scratchOffset + 1] = (ushort)(1 + q0);

                extended = q1 - (bias >> 2) > 32;
                extension = extended ? (uint)value & 0x0f : 0;
                vlc.Advance64(extended ? 4u : 0);
                q1 += extension << 2;
                Require(q1 < ushort.MaxValue, "An HT cleanup UVLC0 q1 value is invalid.");
                scratch[scratchOffset + 3] = (ushort)(1 + q1);
                scratchOffset += 4;
            }

            scratch[scratchOffset] = 0;
            scratch[scratchOffset + 1] = 0;

            for (uint y = 2; y < height; y += 2)
            {
                x = 0;
                scratchOffset = checked((int)(y >> 1) * stride);
                context = 0;
                while (x < width)
                {
                    int above = scratchOffset - stride;
                    context |= ((uint)(scratch[above] & 0xa0) << 2);
                    context |= ((uint)(scratch[above + 2] & 0x20) << 4);
                    ulong value = vlc.Fetch64();
                    ushort first = Tables.Vlc1[context + ((uint)value & 0x7f)];
                    if (context == 0)
                    {
                        run -= 2;
                        first = run == uint.MaxValue ? first : (ushort)0;
                        if ((int)run < 0)
                        {
                            mel.GetRun(out run, out bool hasOne);
                            run = hasOne ? (run << 1) | 1u : run << 1;
                        }
                    }

                    scratch[scratchOffset] = first;
                    x += 2;
                    context = ((uint)(first & 0x40) << 2) | ((uint)(first & 0x80) << 1);
                    context |= scratch[above] & 0x80u;
                    context |= ((uint)(scratch[above + 2] & 0xa0) << 2);
                    context |= ((uint)(scratch[above + 4] & 0x20) << 4);
                    value = vlc.Advance64((uint)first & 7);

                    ushort second = Tables.Vlc1[context + ((uint)value & 0x7f)];
                    if (context == 0 && x < width)
                    {
                        run -= 2;
                        second = run == uint.MaxValue ? second : (ushort)0;
                        if ((int)run < 0)
                        {
                            mel.GetRun(out run, out bool hasOne);
                            run = hasOne ? (run << 1) | 1u : run << 1;
                        }
                    }

                    second = x < width ? second : (ushort)0;
                    scratch[scratchOffset + 2] = second;
                    x += 2;
                    context = ((uint)(second & 0x40) << 2) | ((uint)(second & 0x80) << 1);
                    context |= scratch[above + 2] & 0x80u;
                    value = vlc.Advance64((uint)second & 7);

                    uint rawIndex = ((uint)(first & 8) << 3) |
                        ((uint)(second & 8) << 4) |
                        ((uint)value & 0x3f);
                    Require(rawIndex < Tables.Uvlc1.Length, "An HT cleanup UVLC1 index is invalid.");
                    int index = (int)rawIndex;
                    ushort entry = Tables.Uvlc1[index];
                    value = vlc.Advance64((uint)entry & 7);
                    entry >>= 3;
                    int suffixLength = entry & 0x0f;
                    uint suffix = (uint)value & Mask32(suffixLength);
                    value = vlc.Advance64((uint)suffixLength);
                    entry >>= 4;
                    int firstSuffixLength = entry & 7;
                    entry >>= 3;
                    uint q0 = ((uint)entry & 7) + (suffix & Mask32(firstSuffixLength));
                    uint q1 = ((uint)entry >> 3) + (suffix >> firstSuffixLength);

                    bool extended = q0 > 32;
                    uint extension = extended ? (uint)value & 0x0f : 0;
                    value = vlc.Advance64(extended ? 4u : 0);
                    q0 += extension << 2;
                    Require(q0 <= ushort.MaxValue, "An HT cleanup UVLC1 q0 value is invalid.");
                    scratch[scratchOffset + 1] = (ushort)q0;

                    extended = q1 > 32;
                    extension = extended ? (uint)value & 0x0f : 0;
                    vlc.Advance64(extended ? 4u : 0);
                    q1 += extension << 2;
                    Require(q1 <= ushort.MaxValue, "An HT cleanup UVLC1 q1 value is invalid.");
                    scratch[scratchOffset + 3] = (ushort)q1;
                    scratchOffset += 4;
                }

                scratch[scratchOffset] = 0;
                scratch[scratchOffset + 1] = 0;
            }
        }

        private static void DecodeCleanupMagnitudes(
            uint width,
            uint height,
            uint p,
            uint missingMsbsPlusTwo,
            int stride,
            ushort[] scratch,
            ulong[] north,
            ulong[] buffer,
            ForwardBitReader magSgn)
        {
            uint x = 0;
            int northOffset = 0;
            ulong previousNorth = 0;
            int scratchOffset = 0;
            int destinationOffset = 0;
            while (x < width)
            {
                uint information = scratch[scratchOffset];
                uint u = scratch[scratchOffset + 1];
                Require(u <= missingMsbsPlusTwo, "An HT codeblock magnitude exponent is invalid.");
                buffer[destinationOffset] = DecodeMagnitudeSample(magSgn, information, 0, u, p, out ulong currentNorth);
                ulong second = DecodeMagnitudeSample(magSgn, information, 1, u, p, out currentNorth);
                if (height > 1)
                {
                    buffer[destinationOffset + stride] = second;
                }

                north[northOffset] = previousNorth | currentNorth;
                previousNorth = 0;
                destinationOffset++;
                x++;
                if (x >= width)
                {
                    northOffset++;
                    break;
                }

                buffer[destinationOffset] = DecodeMagnitudeSample(magSgn, information, 2, u, p, out currentNorth);
                second = DecodeMagnitudeSample(magSgn, information, 3, u, p, out currentNorth);
                if (height > 1)
                {
                    buffer[destinationOffset + stride] = second;
                }

                previousNorth = currentNorth;
                destinationOffset++;
                x++;
                scratchOffset += 2;
                northOffset++;
            }

            north[northOffset] = previousNorth;

            for (uint y = 2; y < height; y += 2)
            {
                x = 0;
                northOffset = 0;
                previousNorth = 0;
                scratchOffset = checked((int)(y >> 1) * stride);
                destinationOffset = checked((int)y * stride);
                while (x < width)
                {
                    uint information = scratch[scratchOffset];
                    uint u = scratch[scratchOffset + 1];
                    uint gamma = information & 0xf0;
                    gamma &= gamma - 0x10;
                    ulong maximumSource = north[northOffset] | north[northOffset + 1] | 2;
                    uint maximumExponent = 63u - LeadingZeroCount(maximumSource);
                    uint kappa = gamma != 0 ? maximumExponent : 1;
                    uint effectiveU = checked(u + kappa);
                    Require(effectiveU <= missingMsbsPlusTwo, "An HT codeblock magnitude exponent is invalid.");

                    buffer[destinationOffset] = DecodeMagnitudeSample(
                        magSgn,
                        information,
                        0,
                        effectiveU,
                        p,
                        out ulong currentNorth);
                    ulong second = DecodeMagnitudeSample(
                        magSgn,
                        information,
                        1,
                        effectiveU,
                        p,
                        out currentNorth);
                    if (y + 1 < height)
                    {
                        buffer[destinationOffset + stride] = second;
                    }

                    north[northOffset] = previousNorth | currentNorth;
                    previousNorth = 0;
                    destinationOffset++;
                    x++;
                    if (x >= width)
                    {
                        northOffset++;
                        break;
                    }

                    buffer[destinationOffset] = DecodeMagnitudeSample(
                        magSgn,
                        information,
                        2,
                        effectiveU,
                        p,
                        out currentNorth);
                    second = DecodeMagnitudeSample(
                        magSgn,
                        information,
                        3,
                        effectiveU,
                        p,
                        out currentNorth);
                    if (y + 1 < height)
                    {
                        buffer[destinationOffset + stride] = second;
                    }

                    previousNorth = currentNorth;
                    destinationOffset++;
                    x++;
                    scratchOffset += 2;
                    northOffset++;
                }

                north[northOffset] = previousNorth;
            }
        }

        private static ulong DecodeMagnitudeSample(
            ForwardBitReader reader,
            uint information,
            int bit,
            uint u,
            uint p,
            out ulong north)
        {
            north = 0;
            if ((information & (1u << (4 + bit))) == 0)
            {
                return 0;
            }

            uint magnitudeBits = u - ((information >> (12 + bit)) & 1u);
            Require(magnitudeBits < 63 && p != 0, "An HT codeblock magnitude width is invalid.");
            ulong value = reader.Fetch64();
            reader.Advance(magnitudeBits);
            ulong result = value << 63;
            north = value & Mask64(magnitudeBits);
            north |= (ulong)((information >> (8 + bit)) & 1u) << (int)magnitudeBits;
            north |= 1;
            result |= (north + 2) << (int)(p - 1);
            return result;
        }

        private static void DecodeSignificancePropagation(
            byte[] source,
            CodeBlock codeBlock,
            uint p,
            int stride,
            int scratchCount,
            ushort[] scratch,
            ulong[] buffer)
        {
            uint width = codeBlock.Width;
            uint height = codeBlock.Height;
            int sigmaStride = checked((int)((((width + 3) >> 2) + 9) & ~7u));
            Require((long)sigmaStride * (((height + 3) >> 2) + 1) <= scratchCount,
                "An HT significance map exceeds its scratch allocation.");

            for (uint y = 0; y < height; y += 4)
            {
                int sourceOffset = checked((int)(y >> 1) * stride);
                int sigmaOffset = checked((int)(y >> 2) * sigmaStride);
                for (uint x = 0; x < width; x += 4, sourceOffset += 4, sigmaOffset++)
                {
                    uint first = ((uint)(scratch[sourceOffset] & 0x30) >> 4) |
                        ((uint)(scratch[sourceOffset] & 0xc0) >> 2);
                    first |= ((uint)(scratch[sourceOffset + 2] & 0x30) << 4) |
                        ((uint)(scratch[sourceOffset + 2] & 0xc0) << 6);
                    uint second = ((uint)(scratch[sourceOffset + stride] & 0x30) >> 2) |
                        (uint)(scratch[sourceOffset + stride] & 0xc0);
                    second |= ((uint)(scratch[sourceOffset + stride + 2] & 0x30) << 6) |
                        ((uint)(scratch[sourceOffset + stride + 2] & 0xc0) << 8);
                    scratch[sigmaOffset] = (ushort)(first | second);
                }

                scratch[sigmaOffset] = 0;
            }

            int sentinelOffset = checked((int)((height + 3) >> 2) * sigmaStride);
            for (uint x = 0; x < width; x += 4)
            {
                scratch[sentinelOffset++] = 0;
            }

            scratch[sentinelOffset] = 0;
            CodeBlockStreams streams = SplitCodeBlockStreams(source, codeBlock);
            ForwardBitReader significance = new ForwardBitReader(
                source,
                streams.RefinementOffset,
                streams.RefinementLength,
                fillWithOnes: false);
            ushort[] previousRow = new ushort[264];

            for (uint y = 0; y < height; y += 4)
            {
                uint rowPattern = 0xffff;
                uint previous = 0;
                int previousOffset = 0;
                int currentOffset = checked((int)(y >> 2) * sigmaStride);
                int dataOffset = checked((int)y * stride);
                if (height - y < 4)
                {
                    rowPattern = 0x7777;
                    if (height - y < 3)
                    {
                        rowPattern = 0x3333;
                        if (height - y < 2)
                        {
                            rowPattern = 0x1111;
                        }
                    }
                }

                for (uint x = 0; x < width; x += 4, currentOffset++, previousOffset++)
                {
                    uint remaining = x + 4 - width;
                    uint pattern = rowPattern >> (int)((remaining < 4 ? remaining : 0) * 4);
                    uint previousSignificance = LoadTwoUShort(previousRow, previousOffset);
                    uint nextSignificance = LoadTwoUShort(scratch, currentOffset + sigmaStride);
                    uint vertical = (previousSignificance & 0x88888888) >> 3;
                    uint currentSignificance = LoadTwoUShort(scratch, currentOffset);
                    vertical |= (nextSignificance & 0x11111111) << 3;
                    uint membership = currentSignificance;
                    membership |= (currentSignificance & 0x77777777) << 1;
                    membership |= (currentSignificance & 0xeeeeeeee) >> 1;
                    membership |= vertical;
                    uint temp = membership;
                    membership |= temp << 4;
                    membership |= temp >> 4;
                    membership |= previous >> 12;
                    membership &= pattern;
                    membership &= ~currentSignificance;

                    uint newSignificance = membership;
                    if (newSignificance != 0)
                    {
                        uint codeword = significance.Fetch();
                        uint consumed = 0;
                        uint columnMask = 0x0f;
                        uint inverseSignificance = ~currentSignificance & pattern;
                        for (int bitIndex = 0; bitIndex < 16; bitIndex += 4, columnMask <<= 4)
                        {
                            if ((columnMask & newSignificance) == 0)
                            {
                                continue;
                            }

                            uint sampleMask = 0x1111u & columnMask;
                            Propagate(ref newSignificance, sampleMask, 0x33u << bitIndex, inverseSignificance, ref codeword, ref consumed);
                            sampleMask <<= 1;
                            Propagate(ref newSignificance, sampleMask, 0x76u << bitIndex, inverseSignificance, ref codeword, ref consumed);
                            sampleMask <<= 1;
                            Propagate(ref newSignificance, sampleMask, 0xecu << bitIndex, inverseSignificance, ref codeword, ref consumed);
                            sampleMask <<= 1;
                            Propagate(ref newSignificance, sampleMask, 0xc8u << bitIndex, inverseSignificance, ref codeword, ref consumed);
                        }

                        if (newSignificance != 0)
                        {
                            ulong value = 3UL << (int)(p - 2);
                            columnMask = 0x0f;
                            for (int column = 0; column < 4; column++, columnMask <<= 4)
                            {
                                if ((columnMask & newSignificance) == 0)
                                {
                                    continue;
                                }

                                int sampleOffset = dataOffset + checked((int)x) + column;
                                uint sampleMask = 0x1111u & columnMask;
                                StoreSignificant(buffer, sampleOffset, newSignificance, sampleMask, value, ref codeword, ref consumed);
                                sampleMask <<= 1;
                                if (y + 1 < height)
                                {
                                    StoreSignificant(buffer, sampleOffset + stride, newSignificance, sampleMask, value, ref codeword, ref consumed);
                                }

                                sampleMask <<= 1;
                                if (y + 2 < height)
                                {
                                    StoreSignificant(buffer, sampleOffset + 2 * stride, newSignificance, sampleMask, value, ref codeword, ref consumed);
                                }

                                sampleMask <<= 1;
                                if (y + 3 < height)
                                {
                                    StoreSignificant(buffer, sampleOffset + 3 * stride, newSignificance, sampleMask, value, ref codeword, ref consumed);
                                }
                            }
                        }

                        significance.Advance(consumed);
                    }

                    newSignificance |= currentSignificance;
                    previousRow[previousOffset] = (ushort)newSignificance;
                    temp = newSignificance;
                    newSignificance |= (temp & 0x7777) << 1;
                    newSignificance |= (temp & 0xeeee) >> 1;
                    previous = (newSignificance | vertical) & 0xf000;
                }
            }
        }

        private static void Propagate(
            ref uint significance,
            uint sampleMask,
            uint expansion,
            uint inverseSignificance,
            ref uint codeword,
            ref uint consumed)
        {
            if ((significance & sampleMask) == 0)
            {
                return;
            }

            significance &= ~sampleMask;
            if ((codeword & 1) != 0)
            {
                significance |= expansion & inverseSignificance;
            }

            codeword >>= 1;
            consumed++;
        }

        private static void StoreSignificant(
            ulong[] buffer,
            int offset,
            uint significance,
            uint sampleMask,
            ulong magnitude,
            ref uint codeword,
            ref uint consumed)
        {
            if ((significance & sampleMask) == 0)
            {
                return;
            }

            buffer[offset] = ((ulong)(codeword & 1) << 63) | magnitude;
            codeword >>= 1;
            consumed++;
        }

        private static void DecodeMagnitudeRefinement(
            byte[] source,
            CodeBlock codeBlock,
            uint p,
            int stride,
            ushort[] significance,
            ulong[] buffer)
        {
            uint width = codeBlock.Width;
            uint height = codeBlock.Height;
            int sigmaStride = checked((int)((((width + 3) >> 2) + 9) & ~7u));
            VlcReverseReader refinement = VlcReverseReader.CreateRefinement(
                source,
                codeBlock.DataOffset,
                checked((int)codeBlock.Length0),
                checked((int)codeBlock.Length1));

            for (uint y = 0; y < height; y += 4)
            {
                int currentOffset = checked((int)(y >> 2) * sigmaStride);
                int dataOffset = checked((int)y * stride);
                for (uint x = 0; x < width; x += 8, currentOffset += 2)
                {
                    uint codeword = refinement.Fetch();
                    uint significant = LoadTwoUShort(significance, currentOffset);
                    uint columnMask = 0x0f;
                    if (significant != 0)
                    {
                        for (int column = 0; column < 8; column++, columnMask <<= 4)
                        {
                            if ((significant & columnMask) == 0)
                            {
                                continue;
                            }

                            int sampleOffset = dataOffset + checked((int)x) + column;
                            uint sampleMask = 0x11111111u & columnMask;
                            for (int row = 0; row < 4; row++)
                            {
                                if ((significant & sampleMask) != 0)
                                {
                                    ulong symbol = (ulong)(1u - (codeword & 1)) << (int)(p - 1);
                                    symbol |= 1UL << (int)(p - 2);
                                    buffer[sampleOffset] ^= symbol;
                                    codeword >>= 1;
                                }

                                sampleMask <<= 1;
                                sampleOffset += stride;
                            }
                        }
                    }

                    refinement.Advance(PopCount(significant));
                }
            }
        }

        private static uint LoadTwoUShort(ushort[] values, int offset)
        {
            return values[offset] | ((uint)values[offset + 1] << 16);
        }

        private static uint PopCount(uint value)
        {
            value -= (value >> 1) & 0x55555555;
            value = (value & 0x33333333) + ((value >> 2) & 0x33333333);
            return ((value + (value >> 4) & 0x0f0f0f0f) * 0x01010101) >> 24;
        }

        private static uint LeadingZeroCount(ulong value)
        {
            Require(value != 0, "An HT magnitude neighborhood is empty.");
            uint count = 0;
            ulong mask = 1UL << 63;
            while ((value & mask) == 0)
            {
                count++;
                mask >>= 1;
            }

            return count;
        }

        private static uint Mask32(int bitCount)
        {
            return bitCount >= 32 ? uint.MaxValue : bitCount == 0 ? 0 : (1u << bitCount) - 1;
        }

        private static ulong Mask64(uint bitCount)
        {
            return bitCount >= 64 ? ulong.MaxValue : bitCount == 0 ? 0 : (1UL << (int)bitCount) - 1;
        }

        private sealed class MelReader
        {
            private static readonly byte[] Exponents = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5 };

            private readonly byte[] _data;
            private readonly int _end;
            private int _position;
            private ulong _bits;
            private int _bitCount;
            private bool _previousFf;
            private int _state;

            public MelReader(byte[] data, int offset, int length)
            {
                _data = data;
                _position = offset;
                _end = checked(offset + length);
            }

            public void GetRun(out uint zeroRun, out bool hasOne)
            {
                int exponent = Exponents[_state];
                if (Read(1) != 0)
                {
                    zeroRun = (1u << exponent) - 1;
                    hasOne = false;
                    if (_state < 12)
                    {
                        _state++;
                    }

                    return;
                }

                zeroRun = exponent == 0 ? 0 : Read(exponent);
                hasOne = true;
                if (_state > 0)
                {
                    _state--;
                }
            }

            private uint Read(int count)
            {
                while (_bitCount < count)
                {
                    byte value;
                    int addedBits = 8;
                    if (_position < _end)
                    {
                        value = _data[_position++];
                        if (_position == _end)
                        {
                            value |= 0x0f;
                        }
                    }
                    else
                    {
                        value = 0xff;
                    }

                    if (_previousFf)
                    {
                        Require(value <= 0x8f, "The HT MEL stream has invalid stuffing.");
                        value &= 0x7f;
                        addedBits = 7;
                    }

                    _bits = (_bits << addedBits) | value;
                    _bitCount += addedBits;
                    _previousFf = addedBits == 8 && value == 0xff;
                }

                if (count == 0)
                {
                    return 0;
                }

                ulong mask = (1UL << count) - 1;
                uint result = (uint)((_bits >> (_bitCount - count)) & mask);
                _bitCount -= count;
                _bits = _bitCount == 0 ? 0 : _bits & ((1UL << _bitCount) - 1);
                return result;
            }
        }

        private sealed class ForwardBitReader
        {
            private readonly ulong[] _words;
            private readonly ulong _realBits;
            private readonly ulong _fill;
            private ulong _cursor;

            public ForwardBitReader(byte[] data, int offset, int length, bool fillWithOnes)
            {
                _words = new ulong[length / 8 + 3];
                _realBits = Unstuff(data, offset, length, _words);
                _fill = fillWithOnes ? ulong.MaxValue : 0;
            }

            public uint Fetch()
            {
                return (uint)Fetch64();
            }

            public ulong Fetch64()
            {
                if (_cursor >= _realBits)
                {
                    return _fill;
                }

                int word = checked((int)(_cursor >> 6));
                int bit = (int)(_cursor & 63);
                ulong value = _words[word] >> bit;
                if (bit != 0)
                {
                    value |= _words[word + 1] << (64 - bit);
                }

                ulong available = _realBits - _cursor;
                if (available < 64 && _fill != 0)
                {
                    value |= ulong.MaxValue << (int)available;
                }

                return value;
            }

            public void Advance(uint bitCount)
            {
                _cursor = checked(_cursor + bitCount);
            }

            private static ulong Unstuff(byte[] data, int offset, int length, ulong[] destination)
            {
                ulong accumulator = 0;
                int accumulatorBits = 0;
                int word = 0;
                ulong totalBits = 0;
                bool previousFf = false;
                for (int i = 0; i < length; i++)
                {
                    byte value = data[offset + i];
                    int bits = previousFf ? 7 : 8;
                    ulong unstuffed = (ulong)(value & ((1 << bits) - 1));
                    int oldBits = accumulatorBits;
                    accumulator |= unstuffed << oldBits;
                    accumulatorBits += bits;
                    totalBits += (uint)bits;
                    if (accumulatorBits >= 64)
                    {
                        destination[word++] = accumulator;
                        accumulatorBits -= 64;
                        accumulator = accumulatorBits != 0 ? unstuffed >> (64 - oldBits) : 0;
                    }

                    previousFf = value == 0xff;
                }

                if (accumulatorBits != 0)
                {
                    destination[word] = accumulator;
                }

                return totalBits;
            }
        }

        private sealed class VlcReverseReader
        {
            private readonly byte[] _data;
            private readonly int _start;
            private int _position;
            private ulong _temporary;
            private uint _bits;
            private bool _unstuff;
            private uint _remaining;

            private VlcReverseReader(byte[] data, int start, int position, uint remaining)
            {
                _data = data;
                _start = start;
                _position = position;
                _remaining = remaining;
            }

            public static VlcReverseReader CreateCleanup(
                byte[] data,
                int offset,
                int cleanupLength,
                int scup)
            {
                Require(scup >= 2 && cleanupLength >= scup, "An HT VLC segment is invalid.");
                VlcReverseReader reader = new VlcReverseReader(
                    data,
                    offset,
                    checked(offset + cleanupLength - 2),
                    checked((uint)scup - 2));
                byte value = data[reader._position--];
                reader._temporary = (ulong)(value >> 4);
                reader._bits = 4u - ((reader._temporary & 7) == 7 ? 1u : 0u);
                reader._unstuff = (value | 0x0f) > 0x8f;

                uint count = 1u + ((uint)reader._position & 3u);
                uint preload = Math.Min(count, reader._remaining);
                for (uint i = 0; i < preload; i++)
                {
                    value = data[reader._position--];
                    uint bits = 8u - (reader._unstuff && (value & 0x7f) == 0x7f ? 1u : 0u);
                    reader._temporary |= (ulong)value << (int)reader._bits;
                    reader._bits += bits;
                    reader._unstuff = value > 0x8f;
                }

                reader._remaining -= preload;
                if (reader._bits <= 32)
                {
                    reader.ReadMore();
                }

                return reader;
            }

            public static VlcReverseReader CreateRefinement(
                byte[] data,
                int offset,
                int cleanupLength,
                int refinementLength)
            {
                VlcReverseReader reader = new VlcReverseReader(
                    data,
                    checked(offset + cleanupLength),
                    checked(offset + cleanupLength + refinementLength - 1),
                    checked((uint)refinementLength))
                {
                    _unstuff = true,
                };
                uint count = 1u + ((uint)reader._position & 3u);
                for (uint i = 0; i < count; i++)
                {
                    ulong value = 0;
                    if (reader._remaining != 0)
                    {
                        value = data[reader._position--];
                        reader._remaining--;
                    }

                    uint bits = 8u - (reader._unstuff && ((uint)value & 0x7f) == 0x7f ? 1u : 0u);
                    reader._temporary |= value << (int)reader._bits;
                    reader._bits += bits;
                    reader._unstuff = value > 0x8f;
                }

                reader.ReadMore();
                return reader;
            }

            public uint Fetch()
            {
                if (_bits < 32)
                {
                    ReadMore();
                    if (_bits < 32)
                    {
                        ReadMore();
                    }
                }

                return (uint)_temporary;
            }

            public ulong Fetch64()
            {
                while (_bits < 32)
                {
                    ReadMore();
                }

                return _temporary;
            }

            public uint Advance(uint bitCount)
            {
                if (bitCount > _bits)
                {
                    bitCount = _bits;
                }

                _temporary >>= (int)bitCount;
                _bits -= bitCount;
                return (uint)_temporary;
            }

            public ulong Advance64(uint bitCount)
            {
                Advance(bitCount);
                return _temporary;
            }

            private void ReadMore()
            {
                if (_bits > 32)
                {
                    return;
                }

                uint value = 0;
                uint outputBits = 0;
                uint temporary = 0;
                bool unstuff = _unstuff;
                if (_remaining > 3)
                {
                    int position = _position - 3;
                    value = (uint)(_data[position] |
                        (_data[position + 1] << 8) |
                        (_data[position + 2] << 16) |
                        (_data[position + 3] << 24));
                    _position -= 4;
                    _remaining -= 4;
                }
                else if (_remaining != 0)
                {
                    int shift = 24;
                    while (_remaining != 0)
                    {
                        Require(_position >= _start, "An HT reverse VLC stream underflowed.");
                        value |= (uint)_data[_position--] << shift;
                        _remaining--;
                        shift -= 8;
                    }
                }

                Append((byte)(value >> 24), ref unstuff, ref temporary, ref outputBits);
                Append((byte)(value >> 16), ref unstuff, ref temporary, ref outputBits);
                Append((byte)(value >> 8), ref unstuff, ref temporary, ref outputBits);
                byte last = (byte)value;
                uint lastBits = unstuff && (last & 0x7f) == 0x7f ? 7u : 8u;
                _unstuff = last > 0x8f;
                temporary |= (uint)last << (int)outputBits;
                outputBits += lastBits;
                _temporary |= (ulong)temporary << (int)_bits;
                _bits += outputBits;
            }

            private static void Append(
                byte value,
                ref bool unstuff,
                ref uint temporary,
                ref uint outputBits)
            {
                uint bits = unstuff && (value & 0x7f) == 0x7f ? 7u : 8u;
                unstuff = value > 0x8f;
                temporary |= (uint)value << (int)outputBits;
                outputBits += bits;
            }
        }

        private sealed class HtTables
        {
            private HtTables(ushort[] vlc0, ushort[] vlc1, ushort[] uvlc0, ushort[] uvlc1, byte[] uvlcBias)
            {
                Vlc0 = vlc0;
                Vlc1 = vlc1;
                Uvlc0 = uvlc0;
                Uvlc1 = uvlc1;
                UvlcBias = uvlcBias;
            }

            public ushort[] Vlc0 { get; }

            public ushort[] Vlc1 { get; }

            public ushort[] Uvlc0 { get; }

            public ushort[] Uvlc1 { get; }

            public byte[] UvlcBias { get; }

            public static HtTables Create()
            {
                ushort[] uvlc0 = new ushort[320];
                ushort[] uvlc1 = new ushort[256];
                byte[] bias = new byte[320];
                BuildUvlcTables(uvlc0, uvlc1, bias);
                return new HtTables(
                    DecodeUShortTable(VlcTable0Base64),
                    DecodeUShortTable(VlcTable1Base64),
                    uvlc0,
                    uvlc1,
                    bias);
            }

            private static void BuildUvlcTables(ushort[] table0, ushort[] table1, byte[] bias)
            {
                byte[] decode =
                {
                    (byte)(3 | (5 << 2) | (5 << 5)),
                    (byte)(1 | (0 << 2) | (1 << 5)),
                    (byte)(2 | (0 << 2) | (2 << 5)),
                    (byte)(1 | (0 << 2) | (1 << 5)),
                    (byte)(3 | (1 << 2) | (3 << 5)),
                    (byte)(1 | (0 << 2) | (1 << 5)),
                    (byte)(2 | (0 << 2) | (2 << 5)),
                    (byte)(1 | (0 << 2) | (1 << 5)),
                };

                for (uint i = 0; i < table0.Length; i++)
                {
                    uint mode = i >> 6;
                    uint vlc = i & 0x3f;
                    if (mode == 0)
                    {
                        continue;
                    }

                    if (mode <= 2)
                    {
                        uint value = decode[vlc & 7];
                        uint prefix = value & 3;
                        uint suffix = (value >> 2) & 7;
                        uint firstSuffix = mode == 1 ? suffix : 0;
                        uint first = mode == 1 ? value >> 5 : 0;
                        uint second = mode == 1 ? 0 : value >> 5;
                        table0[i] = (ushort)(prefix | (suffix << 3) | (firstSuffix << 7) | (first << 10) | (second << 13));
                        continue;
                    }

                    uint firstValue = decode[vlc & 7];
                    vlc >>= (int)(firstValue & 3);
                    uint secondValue = decode[vlc & 7];
                    uint totalPrefix;
                    uint firstSuffixLength;
                    uint totalSuffix;
                    uint firstU;
                    uint secondU;
                    if (mode == 3 && (firstValue & 3) == 3)
                    {
                        totalPrefix = (firstValue & 3) + 1;
                        firstSuffixLength = (firstValue >> 2) & 7;
                        totalSuffix = firstSuffixLength;
                        firstU = firstValue >> 5;
                        secondU = (vlc & 1) + 1;
                        bias[i] = 4;
                    }
                    else
                    {
                        totalPrefix = (firstValue & 3) + (secondValue & 3);
                        firstSuffixLength = (firstValue >> 2) & 7;
                        totalSuffix = firstSuffixLength + ((secondValue >> 2) & 7);
                        firstU = firstValue >> 5;
                        secondU = secondValue >> 5;
                        if (mode > 3)
                        {
                            firstU += 2;
                            secondU += 2;
                            bias[i] = 10;
                        }
                    }

                    table0[i] = (ushort)(totalPrefix | (totalSuffix << 3) | (firstSuffixLength << 7) | (firstU << 10) | (secondU << 13));
                }

                for (uint i = 0; i < table1.Length; i++)
                {
                    uint mode = i >> 6;
                    uint vlc = i & 0x3f;
                    if (mode == 0)
                    {
                        continue;
                    }

                    if (mode <= 2)
                    {
                        uint value = decode[vlc & 7];
                        uint prefix = value & 3;
                        uint suffix = (value >> 2) & 7;
                        uint firstSuffix = mode == 1 ? suffix : 0;
                        uint first = mode == 1 ? value >> 5 : 0;
                        uint second = mode == 1 ? 0 : value >> 5;
                        table1[i] = (ushort)(prefix | (suffix << 3) | (firstSuffix << 7) | (first << 10) | (second << 13));
                        continue;
                    }

                    uint firstValue = decode[vlc & 7];
                    vlc >>= (int)(firstValue & 3);
                    uint secondValue = decode[vlc & 7];
                    uint totalPrefix = (firstValue & 3) + (secondValue & 3);
                    uint firstSuffixLength = (firstValue >> 2) & 7;
                    uint totalSuffix = firstSuffixLength + ((secondValue >> 2) & 7);
                    uint firstU = firstValue >> 5;
                    uint secondU = secondValue >> 5;
                    table1[i] = (ushort)(totalPrefix | (totalSuffix << 3) | (firstSuffixLength << 7) | (firstU << 10) | (secondU << 13));
                }
            }

            private static ushort[] DecodeUShortTable(string encoded)
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                Require(bytes.Length == 2048, "An embedded HT VLC table has an invalid size.");
                ushort[] result = new ushort[1024];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
                }

                return result;
            }

            // ITU-T T.814 Tables A.20/A.21, derived from OpenJPH's BSD-2-Clause
            // table generators and stored as little-endian ushort lookup data.
            private const string VlcTable0Base64 =
                "IwClAEMAZgCDAO6oFADf2CMAvhBDAP/1gwB+IFUAX1EjADUAQwBORIMAzsQUAM/MIwD+4kMA/5mDAJYAxQA/MSMApQBDAF5EgwDOyBQA3xEjAP70QwD//IMA" +
                "ngBVAHcAIwA1AEMA//GDAK6IFAC3ACMA/vhDAO/kgwCOiMUAHxEjAKUAQwBmAIMA7qgUAN9UIwC+EEMA7yKDAH4gVQB/IiMANQBDAE5EgwDOxBQAvxEjAP7i" +
                "QwD3AIMAlgDFAD8iIwClAEMAXkSDAM7IFADXACMA/vRDAP+6gwCeAFUAbwAjADUAQwD/5oMArogUAK+iIwD++EMA5wCDAI6IxQAvIgIAxQCEAH4gAgDOxCQA" +
                "9wACAP6iRABWAAIAngAUANcAAgC+EIQAZgACAK6IJADfEQIA7qhEADYAAgCOiBQAHxECAMUAhABuAAIAzogkAP+IAgD+uEQATkQCAJYAFAC3AAIA/uSEAF5E" +
                "AgCmACQA5wACAN5URAAuIgIAPgAUAHcAAgDFAIQAfiACAM7EJAD/8QIA/qJEAFYAAgCeABQAvxECAL4QhABmAAIArogkAO8iAgDuqEQANgACAI6IFAB/IgIA" +
                "xQCEAG4AAgDOiCQA7+QCAP64RABORAIAlgAUAK+iAgD+5IQAXkQCAKYAJADf2AIA3lREAC4iAgA+ABQAX1ECAFUAhABmAAIA3ogkAP8yAgD+EUQATkQCAK4A" +
                "FAC3AAIAfjGEAF5RAgDGACQA1wACAO4gRAAeEQIAngAUAHcAAgBVAIQAXlQCAM5EJADnAAIA/vFEADYAAgCmABQAX1UCAP50hAA+EQIAviAkAH90AgDexEQA" +
                "//gCAJYAFAAvIgIAVQCEAGYAAgDeiCQA9wACAP4RRABORAIArgAUAI+IAgB+MYQAXlECAMYAJADPyAIA7iBEAB4RAgCeABQAbwACAFUAhABeVAIAzkQkAN/R" +
                "AgD+8UQANgACAKYAFAB/IgIA/nSEAD4RAgC+ICQAvyICAN7ERADvIgIAlgAUAD8yAwDe1P30//wUAD4RVQCPiAMAvjKFAOcAJQBeUf6qf3IDAM5E/fjvRBQA" +
                "fmRFAK+iAwCmAF1V35n98TYA/vVvYgMA3tH99P/mFAB+cVUAv7EDAK6IhQDf1SUATkT+8n9mAwDGAP347+IUAF5URQCfEQMAlgBdVc/I/fEeEe7IZwADAN7U" +
                "/fT/8xQAPhFVAL8RAwC+MoUA39glAF5R/qovIgMAzkT9+PcAFAB+ZEUAn5gDAKYAXVXXAP3xNgD+9W9EAwDe0f30/7kUAH5xVQC3AAMAroiFAN/cJQBORP7y" +
                "dwADAMYA/fjv5BQAXlRFAH9zAwCWAF1Vv7j98R4R7sg/MgIApQCEAH5AAgDeECQA3xECAP5yRABWAAIArqgUAL+yAgCWAIQAZgACAMYAJADnAAIA7shEAC4i" +
                "AgCOiBQAdwACAKUAhABuAAIAzogkAPcAAgD+kUQANgACAK6iFACvqgIA/riEAF4AAgC+ACQAz8QCAO5ERAD/9AIAPiIUAB8RAgClAIQAfkACAN4QJAD/mQIA" +
                "/nJEAFYAAgCuqBQAtwACAJYAhABmAAIAxgAkANcAAgDuyEQALiICAI6IFABPRAIApQCEAG4AAgDOiCQA7+ICAP6RRAA2AAIArqIUAH9EAgD+uIQAXgACAL4A" +
                "JACfAAIA7kREAP92AgA+IhQAPzEDAMYAhQD/2f3yfmT+8b+ZAwCuoiUA72b99FYA7uJ/cwMAvphFAPcA/fhmAP52n4gDAI6IFQDf1aUALiLemE9EAwC+soUA" +
                "//z98m4ilgC3AAMArqolAN/R/fQ2AN7Ub2QDAK6oRQDv6v34XkTu6H9xAwA+MhUAz8SlAP/6zog/MQMAxgCFAP93/fJ+ZP7xv7MDAK6iJQDnAP30VgDu4ncA" +
                "AwC+mEUA7+T9+GYA/nZ/ZgMAjogVANcApQAuIt6YPzMDAL6yhQD/df3ybiKWAJ+RAwCuqiUA35n99DYA3tRfUQMArqhFAO/s/fheRO7of3IDAD4yFQC/saUA" +
                "//POiB8RAwDeVP3yHhEUAH5k/vjPzAMAvpFFAO8iJQAuIv7zj4gDAMYAhQD3ABQAXhH+/K+oAwCmADUA38j98T4x/mZvZAMAzsj98v/1FABmAP70v7oDAK4i" +
                "RQDnACUAPjL+6n9zAwC+soUA31UUAFYAfnGfEQMAlgA1AM/E/fE+M+7oT0QDAN5U/fIeERQAfmT++L+ZAwC+kUUA7+IlAC4i/vN/ZgMAxgCFAO/kFABeEf78" +
                "n5gDAKYANQDXAP3xPjH+Zm8iAwDOyP3y/7kUAGYA/vS3AAMAriJFAN/RJQA+Mv7qdwADAL6yhQDv7BQAVgB+cX9yAwCWADUAv7j98T4z7uhfVPzx3tH9+tcA" +
                "/PgWAP3/f3T89H5x/fO/s/zy7+ru6E9E/PGuIgUAv7j8+PcA/vx3APz0XhH99X91/PLf2O7iPzP88b6y/frPiPz4//v9/39z/PRuAP3ztwD88u9m/vk/Mfzx" +
                "ngAFAL+6/Pj//f72ZwD89CYA/fWPiPzy39ze1C8i/PHe0f36z8T8+BYA/f9/cvz0fnH987+Z/PLv7O7oRwD88a4iBQCnAPz4//f+/FcA/PReEf31lwD88t/V" +
                "7uI3APzxvrL9+scA/Pj//v3/f2b89G4A/fOvqPzy5wD++T8y/PGeAAUAv7H8+O/k/vZfVPz0JgD99YcA/PLfmd7UHxE=";
            private const string VlcTable1Base64 =
                "EwBlAEMA3gCDAI2IIwBORBMApQBDAK6IgwA1ACMA1wATAMUAQwCeAIMAVQAjAC4iEwCVAEMAfgCDAP4QIwB3ABMAZQBDAM6IgwCNiCMAHhETAKUAQwBeAIMA" +
                "NQAjAOcAEwDFAEMAvgCDAFUAIwD/ERMAlQBDAD4AgwDuQCMAr6ITAGUAQwDeAIMAjYgjAE5EEwClAEMAroiDADUAIwDvRBMAxQBDAJ4AgwBVACMALiITAJUA" +
                "QwB+AIMA/hAjALcAEwBlAEMAzoiDAI2IIwAeERMApQBDAF4AgwA1ACMAz8QTAMUAQwC+AIMAVQAjAPcAEwCVAEMAPgCDAO5AIwBvAAEAhAABAFYAAQAUAAEA" +
                "1wABACQAAQCWAAEARQABAHcAAQCEAAEAxgABABQAAQCPiAEAJAABAPcAAQA1AAEALyIBAIQAAQD+QAEAFAABALcAAQAkAAEAvwABAEUAAQBnAAEAhAABAKYA" +
                "AQAUAAEAT0QBACQAAQDnAAEANQABAD8RAQCEAAEAVgABABQAAQDPAAEAJAABAJYAAQBFAAEAbwABAIQAAQDGAAEAFAABAJ8AAQAkAAEA7wABADUAAQA/MgEA" +
                "hAABAP5AAQAUAAEArwABACQAAQD/RAEARQABAF8AAQCEAAEApgABABQAAQB/AAEAJAABAN8AAQA1AAEAHxEBACQAAQBWAAEAhQABAL8AAQAUAAEA9wABAMYA" +
                "AQB3AAEAJAABAP/4AQBFAAEAfwABABQAAQDfAAEApgABAD8xAQAkAAEALiIBAIUAAQC3AAEAFAABAO9EAQCuogEAZwABACQAAQD/UQEARQABAJcAAQAUAAEA" +
                "zwABADYAAQA/IgEAJAABAFYAAQCFAAEAv7IBABQAAQDvQAEAxgABAG8AAQAkAAEA/3IBAEUAAQCfAAEAFAABANcAAQCmAAEAT0QBACQAAQAuIgEAhQABAK+o" +
                "AQAUAAEA5wABAK6iAQBfAAEAJAABAP9EAQBFAAEAj4gBABQAAQCvqgEANgABAB8RAgD++CQAVgACALYAhQD/ZgIAzgAUAB4RAgCWADUAr6gCAPYAJAA+MQIA" +
                "pgBFAL+zAgC+shQA//UCAGYAflFfVAIA/vIkAC4iAgCuIoUA70QCAMYAFAD/9AIAdgA1AH9EAgDeQCQAPjICAJ4ARQDXAAIAvogUAP/6AgBeEf7xT0QCAP74" +
                "JABWAAIAtgCFAO/IAgDOABQAHhECAJYANQCPiAIA9gAkAD4xAgCmAEUA30QCAL6yFAD/qAIAZgB+UW8AAgD+8iQALiICAK4ihQDnAAIAxgAUAO/iAgB2ADUA" +
                "f3ICAN5AJAA+MgIAngBFAL+xAgC+iBQA/3MCAF4R/vE/MwEAhAABAO4gAQDFAAEAz8QBAEQAAQD/MgEAFQABAI+IAQCEAAEAZgABACUAAQCvAAEARAABAO8i" +
                "AQCmAAEAXwABAIQAAQBORAEAxQABAM/MAQBEAAEA9wABABUAAQBvAAEAhAABAFYAAQAlAAEAnwABAEQAAQDfAAEA/jABAC8iAQCEAAEA7iABAMUAAQDPyAEA" +
                "RAABAP8RAQAVAAEAdwABAIQAAQBmAAEAJQABAH8AAQBEAAEA5wABAKYAAQA3AAEAhAABAE5EAQDFAAEAtwABAEQAAQC/AAEAFQABAD8AAQCEAAEAVgABACUA" +
                "AQCXAAEARAABANcAAQD+MAEAHxECAO6oRACOiAIA1gDFAP/zAgD+/CUAPgACALYAVQDf2AIA/vhEAGYAAgB+IIUA/5kCAOYA9QA2AAIApgAVAJ8AAgD+8kQA" +
                "dgACAM5ExQD/dgIA/vElAE5EAgCuAFUAz8gCAP70RABeRAIAvhCFAO/kAgDeVPUAHhECAJYAFQAvIgIA7qhEAI6IAgDWAMUA//oCAP78JQA+AAIAtgBVAL8R" +
                "AgD++EQAZgACAH4ghQDvIgIA5gD1ADYAAgCmABUAfyICAP7yRAB2AAIAzkTFAP/VAgD+8SUATkQCAK4AVQBvAAIA/vREAF5EAgC+EIUA3xECAN5U9QAeEQIA" +
                "lgAVAF9RAwD2ABQAHhFEAI6IpQDf1AMArqJVAP92JAA+IrYAr6oDAOYAFAD/9UQAZgCFAM/MAwCeAMUA70QkADYA/vh/MQMA7ugUAP/xRAB2AKUAz8QDAH4i" +
                "VQDf0SQATkT+9F9RAwDWABQA7+JEAF5EhQC/IgMAlgDFAN/IJAAuIv7ybyIDAPYAFAAeEUQAjoilAL+xAwCuolUA/zMkAD4itgCvqAMA5gAUAP+5RABmAIUA" +
                "v6gDAJ4AxQDv5CQANgD++G9kAwDu6BQA//xEAHYApQDPyAMAfiJVAO/qJABORP70f3QDANYAFAD/+kQAXkSFAL+yAwCWAMUA30QkAC4i/vI/MfMA/vr98TYA" +
                "BAC+MnUA3xHzAN5U/fLv5NUAfnH+/H9z8wD+8/34HhEEAJYAVQC/sfMAzgC1AN/Y/fRmAP65X1TzAP52/fEmAAQApgB1AJ8A8wCuAP3y//fVAEYA/vV/dPMA" +
                "5gD9+BYABACGAFUAj4jzAMYAtQDv4v30XhHuqD8R8wD++v3xNgAEAL4ydQDf0fMA3lT98v/71QB+cf78f0TzAP7z/fgeEQQAlgBVAH9y8wDOALUA7yL99GYA" +
                "/rlPRPMA/nb98SYABACmAHUAvxHzAK4A/fL//9UARgD+9T8y8wDmAP34FgAEAIYAVQBvAPMAxgC1AL+4/fReEe6oLyI=";
        }
    }
}
