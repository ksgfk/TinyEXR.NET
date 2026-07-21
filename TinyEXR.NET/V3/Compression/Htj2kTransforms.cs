using System;
using System.Buffers;
using System.Buffers.Binary;

namespace TinyEXR.V3.Codecs
{
    internal static partial class Htj2kDecoder
    {
        private static Plane[] AllocatePlanes(Profile profile)
        {
            Plane[] planes = new Plane[profile.ComponentCount];
            try
            {
                for (int component = 0; component < planes.Length; component++)
                {
                    Size size = ComponentSize(profile, component);
                    Require(size.Width <= int.MaxValue && size.Height <= int.MaxValue,
                        "A JPEG 2000 component exceeds managed dimensions.");
                    long elementCount = checked((long)size.Width * size.Height);
                    Require(elementCount <= int.MaxValue, "A JPEG 2000 component exceeds managed storage.");
                    int count = (int)elementCount;
                    long[] data = ArrayPool<long>.Shared.Rent(count);
                    Array.Clear(data, 0, count);
                    planes[component] = new Plane(size.Width, size.Height, data);
                }

                return planes;
            }
            catch
            {
                ReturnPlanes(planes);
                throw;
            }
        }

        private static void ReturnPlanes(Plane[] planes)
        {
            for (int component = 0; component < planes.Length; component++)
            {
                Plane? plane = planes[component];
                if (plane != null)
                {
                    ArrayPool<long>.Shared.Return(plane.Data);
                }
            }
        }

        private static void ScatterCodeBlock(
            Profile profile,
            CodeBlock codeBlock,
            BandGeometry band,
            long[] coefficients,
            Plane plane)
        {
            uint rowOffset;
            uint columnOffset;
            if (codeBlock.Band == 0)
            {
                Require(codeBlock.Resolution == 0, "The LL subband occurs outside resolution zero.");
                rowOffset = 0;
                columnOffset = 0;
            }
            else
            {
                Require(codeBlock.Resolution > 0 && codeBlock.Band <= 3,
                    "An HT codeblock has an invalid detail subband.");
                Size resolutionSize = ResolutionSize(
                    new Size(plane.Width, plane.Height),
                    profile.NumDecompositions,
                    (uint)codeBlock.Resolution);
                rowOffset = codeBlock.Band >= 2 ? (resolutionSize.Height + 1) >> 1 : 0;
                columnOffset = codeBlock.Band == 1 || codeBlock.Band == 3
                    ? (resolutionSize.Width + 1) >> 1
                    : 0;
            }

            Require(codeBlock.X <= band.Width && codeBlock.Y <= band.Height &&
                codeBlock.Width <= band.Width - codeBlock.X &&
                codeBlock.Height <= band.Height - codeBlock.Y,
                "An HT codeblock extends outside its subband.");
            Require(rowOffset + codeBlock.Y <= plane.Height &&
                codeBlock.Height <= plane.Height - (rowOffset + codeBlock.Y) &&
                columnOffset + codeBlock.X <= plane.Width &&
                codeBlock.Width <= plane.Width - (columnOffset + codeBlock.X),
                "An HT codeblock extends outside its component plane.");
            Require(coefficients.Length >= checked((int)(codeBlock.Width * codeBlock.Height)),
                "An HT codeblock coefficient allocation is too small.");

            for (uint y = 0; y < codeBlock.Height; y++)
            {
                int sourceOffset = checked((int)(y * codeBlock.Width));
                int destinationOffset = checked((int)((rowOffset + codeBlock.Y + y) * plane.Width +
                    columnOffset + codeBlock.X));
                Array.Copy(coefficients, sourceOffset, plane.Data, destinationOffset, checked((int)codeBlock.Width));
            }
        }

        private static void PostprocessPlanes(Profile profile, Plane[] planes)
        {
            int temporaryLength = 0;
            foreach (Plane plane in planes)
            {
                temporaryLength = Math.Max(temporaryLength, plane.ElementCount);
            }

            long[] temporary = ArrayPool<long>.Shared.Rent(temporaryLength);
            try
            {
                foreach (Plane plane in planes)
                {
                    Inverse53TwoDimensional(
                        plane.Data,
                        plane.Width,
                        plane.Height,
                        profile.NumDecompositions,
                        temporary);
                }
            }
            finally
            {
                ArrayPool<long>.Shared.Return(temporary);
            }

            if (profile.MultipleComponentTransform != 0)
            {
                Require(planes.Length >= 3 &&
                    planes[0].Width == planes[1].Width && planes[0].Width == planes[2].Width &&
                    planes[0].Height == planes[1].Height && planes[0].Height == planes[2].Height,
                    "The JPEG 2000 reversible color transform components have different dimensions.");
                for (int i = 0; i < planes[0].ElementCount; i++)
                {
                    long luminance = planes[0].Data[i];
                    long blueDifference = planes[1].Data[i];
                    long redDifference = planes[2].Data[i];
                    long green = luminance - FloorDividePowerOfTwo(blueDifference + redDifference, 2);
                    planes[0].Data[i] = redDifference + green;
                    planes[1].Data[i] = green;
                    planes[2].Data[i] = blueDifference + green;
                }
            }

            for (int component = 0; component < planes.Length; component++)
            {
                byte nlt = profile.NltType[component];
                Require(nlt == 0 || nlt == 3, "A JPEG 2000 component has an invalid nonlinear transform.");
                if (nlt != 3)
                {
                    continue;
                }

                int bitDepth = (profile.Ssiz[component] & 0x7f) + 1;
                Require(bitDepth >= 1 && bitDepth <= 32, "A JPEG 2000 NLT precision is invalid.");
                long bias = (1L << (bitDepth - 1)) + 1;
                long[] data = planes[component].Data;
                for (int i = 0; i < planes[component].ElementCount; i++)
                {
                    if (data[i] < 0)
                    {
                        data[i] = -data[i] - bias;
                    }
                }
            }
        }

        private static void Inverse53TwoDimensional(
            long[] data,
            uint width,
            uint height,
            uint levels,
            long[] temporary)
        {
            Require(levels <= 32, "The JPEG 2000 wavelet level count is invalid.");
            if (width == 0 || height == 0 || levels == 0)
            {
                return;
            }

            for (uint level = levels; level > 0; level--)
            {
                uint reconstructedWidth = DivideCeilingPowerOfTwo(width, level - 1);
                uint reconstructedHeight = DivideCeilingPowerOfTwo(height, level - 1);
                Require(reconstructedWidth != 0 && reconstructedHeight != 0,
                    "A JPEG 2000 wavelet level has zero dimensions.");
                uint lowWidth = (reconstructedWidth + 1) / 2;
                uint highWidth = reconstructedWidth / 2;
                uint lowHeight = (reconstructedHeight + 1) / 2;
                uint highHeight = reconstructedHeight / 2;
                Require((long)reconstructedWidth * reconstructedHeight <= temporary.Length,
                    "A JPEG 2000 inverse wavelet workspace is too small.");

                for (uint y = 0; y < reconstructedHeight; y++)
                {
                    int sourceOffset = checked((int)(y * width));
                    int destinationOffset = checked((int)(y * reconstructedWidth));
                    Inverse53OneDimensional(
                        data,
                        sourceOffset,
                        lowWidth,
                        sourceOffset + checked((int)lowWidth),
                        highWidth,
                        temporary,
                        destinationOffset,
                        reconstructedWidth);
                }

                if (highHeight == 0)
                {
                    Array.Copy(temporary, 0, data, 0, checked((int)reconstructedWidth));
                    continue;
                }

                for (uint low = 0; low < lowHeight; low++)
                {
                    uint leftHigh = low > 0 ? low - 1 : 0;
                    uint rightHigh = low < highHeight ? low : highHeight - 1;
                    int lowOffset = checked((int)(low * reconstructedWidth));
                    int leftOffset = checked((int)((lowHeight + leftHigh) * reconstructedWidth));
                    int rightOffset = checked((int)((lowHeight + rightHigh) * reconstructedWidth));
                    int outputOffset = checked((int)(2 * low * width));
                    for (uint column = 0; column < reconstructedWidth; column++)
                    {
                        data[outputOffset + column] = temporary[lowOffset + column] -
                            FloorDividePowerOfTwo(
                                temporary[leftOffset + column] + temporary[rightOffset + column] + 2,
                                2);
                    }
                }

                for (uint high = 0; high < highHeight; high++)
                {
                    int highOffset = checked((int)((lowHeight + high) * reconstructedWidth));
                    int firstEvenOffset = checked((int)(2 * high * width));
                    int secondEvenOffset = checked((int)(2 * (high + 1 < lowHeight ? high + 1 : high) * width));
                    int outputOffset = checked((int)((2 * high + 1) * width));
                    for (uint column = 0; column < reconstructedWidth; column++)
                    {
                        data[outputOffset + column] = temporary[highOffset + column] +
                            FloorDividePowerOfTwo(
                                data[firstEvenOffset + column] + data[secondEvenOffset + column],
                                1);
                    }
                }
            }
        }

        private static void Inverse53OneDimensional(
            long[] source,
            int lowOffset,
            uint lowCount,
            int highOffset,
            uint highCount,
            long[] destination,
            int destinationOffset,
            uint outputCount)
        {
            Require(lowCount == (outputCount + 1) / 2 && highCount == outputCount / 2,
                "A JPEG 2000 wavelet row has inconsistent low/high dimensions.");
            if (outputCount == 0)
            {
                return;
            }

            if (highCount == 0)
            {
                destination[destinationOffset] = source[lowOffset];
                return;
            }

            for (uint i = 0; i < lowCount; i++)
            {
                long left = source[highOffset + checked((int)(i > 0 ? i - 1 : 0))];
                long right = source[highOffset + checked((int)(i < highCount ? i : highCount - 1))];
                destination[destinationOffset + checked((int)(2 * i))] = source[lowOffset + i] -
                    FloorDividePowerOfTwo(left + right + 2, 2);
            }

            for (uint i = 0; i < highCount; i++)
            {
                long firstEven = destination[destinationOffset + checked((int)(2 * i))];
                long secondEven = destination[destinationOffset + checked((int)(2 * (i + 1 < lowCount ? i + 1 : i)))];
                destination[destinationOffset + checked((int)(2 * i + 1))] = source[highOffset + i] +
                    FloorDividePowerOfTwo(firstEven + secondEven, 1);
            }
        }

        private static long FloorDividePowerOfTwo(long value, int shift)
        {
            long divisor = 1L << shift;
            long quotient = value / divisor;
            if (value < 0 && value % divisor != 0)
            {
                quotient--;
            }

            return quotient;
        }

        private static void StorePlanes(
            DecodeContext context,
            Profile profile,
            ushort[] channelMap,
            Plane[] planes,
            Span<byte> destination)
        {
            int[] componentForChannel = new int[context.Channels.Count];
            Array.Fill(componentForChannel, -1);
            for (int component = 0; component < channelMap.Length; component++)
            {
                componentForChannel[channelMap[component]] = component;
            }

            int offset = 0;
            for (int y = context.Region.MinY; y <= context.Region.MaxY; y++)
            {
                for (int fileChannel = 0; fileChannel < context.Channels.Count; fileChannel++)
                {
                    Channel channel = context.Channels[fileChannel];
                    if (y % channel.YSampling != 0)
                    {
                        continue;
                    }

                    int component = componentForChannel[fileChannel];
                    Require(component >= 0, "The HTJ2K channel map omits an EXR channel.");
                    Plane plane = planes[component];
                    long sampleCount = ModelValidation.CountSampleLocations(
                        context.Region.MinX,
                        context.Region.MaxX,
                        channel.XSampling);
                    long row = ModelValidation.CountSampleLocations(
                        context.Region.MinY,
                        y,
                        channel.YSampling) - 1;
                    Require(row >= 0 && row < plane.Height && sampleCount >= 0 && sampleCount <= plane.Width,
                        "An HTJ2K component row does not match its EXR channel.");

                    int sourceOffset = checked((int)((uint)row * plane.Width));
                    for (int x = 0; x < sampleCount; x++)
                    {
                        StoreSample(destination, ref offset, channel.PixelType, plane.Data[sourceOffset + x]);
                    }
                }
            }

            Require(offset == destination.Length, "The HTJ2K decoder produced an inconsistent canonical byte count.");
        }

        private static void StoreSample(Span<byte> destination, ref int offset, PixelType pixelType, long value)
        {
            switch (pixelType)
            {
                case PixelType.Half:
                    if (value < short.MinValue || value > ushort.MaxValue || offset > destination.Length - 2)
                    {
                        Require(false, $"An HTJ2K HALF sample ({value}) is outside its native range.");
                    }

                    BinaryPrimitives.WriteUInt16LittleEndian(
                        destination.Slice(offset, 2),
                        unchecked((ushort)(short)value));
                    offset += 2;
                    break;
                case PixelType.UInt:
                    Require(value >= 0 && value <= uint.MaxValue && offset <= destination.Length - 4,
                        "An HTJ2K UINT sample is outside its native range.");
                    BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), (uint)value);
                    offset += 4;
                    break;
                case PixelType.Float:
                    Require(value >= int.MinValue && value <= int.MaxValue && offset <= destination.Length - 4,
                        "An HTJ2K FLOAT sample is outside its native range.");
                    BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), (int)value);
                    offset += 4;
                    break;
                default:
                    Unsupported($"EXR pixel type '{pixelType}' is not supported by HTJ2K.");
                    break;
            }
        }

        private sealed class Plane
        {
            public Plane(uint width, uint height, long[] data)
            {
                Width = width;
                Height = height;
                Data = data;
                ElementCount = checked((int)((long)width * height));
                Require(data.Length >= ElementCount, "A JPEG 2000 plane allocation is too small.");
            }

            public uint Width { get; }

            public uint Height { get; }

            public long[] Data { get; }

            public int ElementCount { get; }
        }
    }
}
