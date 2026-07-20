using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyEXR.V3
{
    /// <summary>
    /// Owned interleaved float pixels and their channel order.
    /// </summary>
    public sealed class InterleavedFloatImage
    {
        private readonly float[] _data;
        private readonly ReadOnlyCollection<string> _channelNames;

        internal InterleavedFloatImage(
            int width,
            int height,
            IEnumerable<string> channelNames,
            float[] data)
        {
            Width = width;
            Height = height;
            List<string> names = new List<string>(channelNames);
            if (names.Count == 0)
            {
                throw new ArgumentException("At least one channel name is required.", nameof(channelNames));
            }

            int expectedLength = checked(checked(width * height) * names.Count);
            if (data.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"The interleaved buffer requires exactly {expectedLength} samples.",
                    nameof(data));
            }

            _channelNames = names.AsReadOnly();
            _data = data;
        }

        public int Width { get; }

        public int Height { get; }

        public int Channels => _channelNames.Count;

        public IReadOnlyList<string> ChannelNames => _channelNames;

        public ReadOnlySpan<float> Data => _data;

        public float GetSample(int x, int y, int channel)
        {
            if ((uint)x >= (uint)Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if ((uint)y >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            if ((uint)channel >= (uint)Channels)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }

            return _data[checked((((y * Width) + x) * Channels) + channel)];
        }
    }

    /// <summary>
    /// Bridges TinyEXR planar flat parts and interleaved float working buffers.
    /// </summary>
    public static class PartConversion
    {
        public static InterleavedFloatImage ToInterleavedFloat(Part part)
        {
            FlatLevel level = GetFullBaseLevel(part);
            int width = checked((int)level.Width);
            int height = checked((int)level.Height);
            int channelCount = part.Header.Channels.Count;
            float[] output = new float[checked(checked(width * height) * channelCount)];
            string[] names = new string[channelCount];
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                Channel channel = part.Header.Channels[channelIndex];
                names[channelIndex] = channel.Name;
                ChannelBuffer buffer = level.GetChannel(channel.Name);
                ValidateBuffer(channel, buffer, level.Region, out int sampledWidth, out int sampledHeight);
                if (sampledWidth == 0 || sampledHeight == 0)
                {
                    continue;
                }

                for (int y = 0; y < height; y++)
                {
                    int absoluteY = checked(level.Region.MinY + y);
                    int sampledY = GetPointSampleCoordinate(
                        level.Region.MinY,
                        absoluteY,
                        channel.YSampling,
                        sampledHeight);
                    for (int x = 0; x < width; x++)
                    {
                        int absoluteX = checked(level.Region.MinX + x);
                        int sampledX = GetPointSampleCoordinate(
                            level.Region.MinX,
                            absoluteX,
                            channel.XSampling,
                            sampledWidth);
                        output[(((y * width) + x) * channelCount) + channelIndex] =
                            ReadSample(buffer, (sampledY * sampledWidth) + sampledX);
                    }
                }
            }

            return new InterleavedFloatImage(width, height, names, output);
        }

        public static Part FromInterleavedFloat(
            ReadOnlySpan<float> source,
            int width,
            int height,
            int channels,
            PixelType destinationType = PixelType.Half,
            Compression compression = Compression.ZIP)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (channels < 1 || channels > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            ModelValidation.ValidateEnum(destinationType, nameof(destinationType));
            ModelValidation.ValidateEnum(compression, nameof(compression));
            int pixelCount = checked(width * height);
            int requiredLength = checked(pixelCount * channels);
            if (source.Length != requiredLength)
            {
                throw new ArgumentException(
                    $"The interleaved source requires exactly {requiredLength} samples.",
                    nameof(source));
            }

            string[][] channelNames =
            {
                Array.Empty<string>(),
                new[] { "Y" },
                new[] { "R", "G" },
                new[] { "R", "G", "B" },
                new[] { "R", "G", "B", "A" },
            };
            List<Channel> descriptions = new List<Channel>(channels);
            List<ChannelBuffer> buffers = new List<ChannelBuffer>(channels);
            float[] plane = new float[pixelCount];
            for (int channelIndex = 0; channelIndex < channels; channelIndex++)
            {
                string name = channelNames[channels][channelIndex];
                descriptions.Add(new Channel(name, destinationType));
                for (int pixel = 0; pixel < pixelCount; pixel++)
                {
                    plane[pixel] = source[(pixel * channels) + channelIndex];
                }

                buffers.Add(new ChannelBuffer(
                    name,
                    destinationType,
                    EncodePlane(plane, destinationType)));
            }

            Box2i dataWindow = new Box2i(0, 0, width - 1, height - 1);
            Header header = new Header(
                PartType.Scanline,
                dataWindow,
                descriptions,
                compression: compression);
            return new Part(
                header,
                new[] { new FlatLevel(0, 0, dataWindow, buffers) },
                isComplete: true);
        }

        public static bool IsLuminanceChroma(Part part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            if (part.Header.IsDeep)
            {
                return false;
            }

            bool hasY = false;
            bool hasRy = false;
            bool hasBy = false;
            bool hasRgb = false;
            foreach (Channel channel in part.Header.Channels)
            {
                switch (channel.Name)
                {
                    case "Y":
                        hasY = true;
                        break;
                    case "RY":
                        hasRy = true;
                        break;
                    case "BY":
                        hasBy = true;
                        break;
                    case "R":
                    case "G":
                    case "B":
                        hasRgb = true;
                        break;
                }
            }

            return hasY && hasRy && hasBy && !hasRgb;
        }

        public static InterleavedFloatImage LuminanceChromaToRgbaFloat(Part part)
        {
            if (!IsLuminanceChroma(part))
            {
                throw new ArgumentException(
                    "The part must contain Y, RY, and BY channels without direct R, G, or B channels.",
                    nameof(part));
            }

            FlatLevel level = GetFullBaseLevel(part);
            int width = checked((int)level.Width);
            int height = checked((int)level.Height);
            int pixelCount = checked(width * height);
            Channel yChannel = GetChannelDescription(part.Header, "Y");
            Channel ryChannel = GetChannelDescription(part.Header, "RY");
            Channel byChannel = GetChannelDescription(part.Header, "BY");
            Channel? alphaChannel = TryGetChannelDescription(part.Header, "A");
            ChannelBuffer yBuffer = level.GetChannel("Y");
            ChannelBuffer ryBuffer = level.GetChannel("RY");
            ChannelBuffer byBuffer = level.GetChannel("BY");
            ChannelBuffer? alphaBuffer = alphaChannel == null ? null : level.GetChannel("A");

            float[] ryFull = UpsampleBilinear(ryChannel, ryBuffer, level.Region, width, height);
            float[] byFull = UpsampleBilinear(byChannel, byBuffer, level.Region, width, height);
            System.Numerics.Vector3 weights = ImageProcessing.GetLuminanceWeights(part.Header.Chromaticities);
            if (weights.Y == 0.0f)
            {
                throw new InvalidDataException("The green luminance weight must not be zero.");
            }

            ValidateBuffer(yChannel, yBuffer, level.Region, out int yWidth, out int yHeight);
            if (alphaChannel != null)
            {
                ValidateBuffer(alphaChannel, alphaBuffer!, level.Region, out _, out _);
            }

            float[] output = new float[checked(pixelCount * 4)];
            for (int y = 0; y < height; y++)
            {
                int absoluteY = checked(level.Region.MinY + y);
                int ySampleY = GetPointSampleCoordinate(
                    level.Region.MinY,
                    absoluteY,
                    yChannel.YSampling,
                    yHeight);
                for (int x = 0; x < width; x++)
                {
                    int absoluteX = checked(level.Region.MinX + x);
                    int ySampleX = GetPointSampleCoordinate(
                        level.Region.MinX,
                        absoluteX,
                        yChannel.XSampling,
                        yWidth);
                    int pixel = (y * width) + x;
                    float luminance = yWidth == 0 || yHeight == 0
                        ? 0.0f
                        : ReadSample(yBuffer, (ySampleY * yWidth) + ySampleX);
                    float red = (ryFull[pixel] + 1.0f) * luminance;
                    float blue = (byFull[pixel] + 1.0f) * luminance;
                    float green =
                        (luminance - (red * weights.X) - (blue * weights.Z)) /
                        weights.Y;
                    int outputOffset = pixel * 4;
                    output[outputOffset] = red;
                    output[outputOffset + 1] = green;
                    output[outputOffset + 2] = blue;
                    output[outputOffset + 3] = alphaChannel == null
                        ? 1.0f
                        : ReadPointSample(alphaChannel, alphaBuffer!, level.Region, absoluteX, absoluteY);
                }
            }

            return new InterleavedFloatImage(
                width,
                height,
                new[] { "R", "G", "B", "A" },
                output);
        }

        private static FlatLevel GetFullBaseLevel(Part part)
        {
            if (part == null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            if (part.Header.IsDeep)
            {
                throw new ArgumentException("Deep parts cannot be converted to flat interleaved pixels.", nameof(part));
            }

            PartLevel materialized = part.GetLevel(0, 0);
            if (!(materialized is FlatLevel level) ||
                level.Region.MinX != part.Header.DataWindow.MinX ||
                level.Region.MinY != part.Header.DataWindow.MinY ||
                level.Region.MaxX != part.Header.DataWindow.MaxX ||
                level.Region.MaxY != part.Header.DataWindow.MaxY)
            {
                throw new ArgumentException("A fully materialized base level is required.", nameof(part));
            }

            return level;
        }

        private static float[] UpsampleBilinear(
            Channel channel,
            ChannelBuffer buffer,
            Box2i region,
            int width,
            int height)
        {
            ValidateBuffer(channel, buffer, region, out int sampledWidth, out int sampledHeight);
            float[] output = new float[checked(width * height)];
            if (sampledWidth == 0 || sampledHeight == 0)
            {
                return output;
            }

            float[] samples = DecodePlane(buffer);
            int firstSampleX = FirstSampleCoordinate(region.MinX, channel.XSampling);
            int firstSampleY = FirstSampleCoordinate(region.MinY, channel.YSampling);
            for (int y = 0; y < height; y++)
            {
                GetLinearCoordinate(
                    region.MinY + y,
                    firstSampleY,
                    channel.YSampling,
                    sampledHeight,
                    out int lowerY,
                    out int upperY,
                    out float fractionY);
                for (int x = 0; x < width; x++)
                {
                    GetLinearCoordinate(
                        region.MinX + x,
                        firstSampleX,
                        channel.XSampling,
                        sampledWidth,
                        out int lowerX,
                        out int upperX,
                        out float fractionX);
                    float top = Lerp(
                        samples[(lowerY * sampledWidth) + lowerX],
                        samples[(lowerY * sampledWidth) + upperX],
                        fractionX);
                    float bottom = Lerp(
                        samples[(upperY * sampledWidth) + lowerX],
                        samples[(upperY * sampledWidth) + upperX],
                        fractionX);
                    output[(y * width) + x] = Lerp(top, bottom, fractionY);
                }
            }

            return output;
        }

        private static void GetLinearCoordinate(
            int coordinate,
            int firstSample,
            int sampling,
            int sampleCount,
            out int lower,
            out int upper,
            out float fraction)
        {
            float grid = (float)(coordinate - firstSample) / sampling;
            if (!(grid > 0.0f))
            {
                lower = 0;
                upper = 0;
                fraction = 0.0f;
                return;
            }

            if (grid >= sampleCount - 1)
            {
                lower = sampleCount - 1;
                upper = lower;
                fraction = 0.0f;
                return;
            }

            lower = (int)grid;
            upper = lower + 1;
            fraction = grid - lower;
        }

        private static float ReadPointSample(
            Channel channel,
            ChannelBuffer buffer,
            Box2i region,
            int x,
            int y)
        {
            ValidateBuffer(channel, buffer, region, out int sampledWidth, out int sampledHeight);
            if (sampledWidth == 0 || sampledHeight == 0)
            {
                return 0.0f;
            }

            int sampledX = GetPointSampleCoordinate(region.MinX, x, channel.XSampling, sampledWidth);
            int sampledY = GetPointSampleCoordinate(region.MinY, y, channel.YSampling, sampledHeight);
            return ReadSample(buffer, (sampledY * sampledWidth) + sampledX);
        }

        private static int GetPointSampleCoordinate(
            int minimum,
            int coordinate,
            int sampling,
            int sampleCount)
        {
            int result = checked((int)ModelValidation.CountSampleLocations(minimum, coordinate, sampling) - 1);
            return Math.Max(0, Math.Min(sampleCount - 1, result));
        }

        private static int FirstSampleCoordinate(int minimum, int sampling)
        {
            int remainder = minimum % sampling;
            if (remainder == 0)
            {
                return minimum;
            }

            return remainder > 0
                ? checked(minimum + sampling - remainder)
                : checked(minimum - remainder);
        }

        private static void ValidateBuffer(
            Channel channel,
            ChannelBuffer buffer,
            Box2i region,
            out int sampledWidth,
            out int sampledHeight)
        {
            if (buffer.PixelType != channel.PixelType)
            {
                throw new InvalidDataException(
                    $"Channel '{channel.Name}' has inconsistent pixel types.");
            }

            sampledWidth = checked((int)ModelValidation.CountSampleLocations(
                region.MinX,
                region.MaxX,
                channel.XSampling));
            sampledHeight = checked((int)ModelValidation.CountSampleLocations(
                region.MinY,
                region.MaxY,
                channel.YSampling));
            int expectedBytes = checked(
                checked(sampledWidth * sampledHeight) *
                ModelValidation.PixelTypeSize(channel.PixelType));
            if (buffer.ByteLength != expectedBytes)
            {
                throw new InvalidDataException(
                    $"Channel '{channel.Name}' contains {buffer.ByteLength} bytes; {expectedBytes} are required.");
            }
        }

        private static float[] DecodePlane(ChannelBuffer buffer)
        {
            int count = checked((int)buffer.SampleCount);
            float[] result = new float[count];
            switch (buffer.PixelType)
            {
                case PixelType.Float:
                    for (int index = 0; index < count; index++)
                    {
                        result[index] = ReadSample(buffer, index);
                    }

                    break;
                case PixelType.UInt:
                    uint[] uints = new uint[count];
                    for (int index = 0; index < count; index++)
                    {
                        uints[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                            buffer.Data.Slice(index * sizeof(uint), sizeof(uint)));
                    }

                    PixelConversion.UIntToFloat(uints, result);
                    break;
                case PixelType.Half:
                    ushort[] half = new ushort[count];
                    for (int index = 0; index < count; index++)
                    {
                        half[index] = BinaryPrimitives.ReadUInt16LittleEndian(
                            buffer.Data.Slice(index * sizeof(ushort), sizeof(ushort)));
                    }

                    PixelConversion.HalfToFloat(half, result);
                    break;
            }

            return result;
        }

        private static float ReadSample(ChannelBuffer buffer, int sampleIndex)
        {
            switch (buffer.PixelType)
            {
                case PixelType.Float:
                    return BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(
                            buffer.Data.Slice(sampleIndex * sizeof(float), sizeof(float))));
                case PixelType.UInt:
                    return BinaryPrimitives.ReadUInt32LittleEndian(
                        buffer.Data.Slice(sampleIndex * sizeof(uint), sizeof(uint)));
                case PixelType.Half:
                    return PixelConversion.HalfBitsToSingle(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            buffer.Data.Slice(sampleIndex * sizeof(ushort), sizeof(ushort))));
                default:
                    return 0.0f;
            }
        }

        private static byte[] EncodePlane(ReadOnlySpan<float> plane, PixelType destinationType)
        {
            int elementSize = ModelValidation.PixelTypeSize(destinationType);
            byte[] data = new byte[checked(plane.Length * elementSize)];
            switch (destinationType)
            {
                case PixelType.Float:
                    for (int index = 0; index < plane.Length; index++)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(
                            data.AsSpan(index * sizeof(float), sizeof(float)),
                            BitConverter.SingleToInt32Bits(plane[index]));
                    }

                    break;
                case PixelType.UInt:
                    uint[] uints = new uint[plane.Length];
                    PixelConversion.FloatToUInt(plane, uints);
                    for (int index = 0; index < uints.Length; index++)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            data.AsSpan(index * sizeof(uint), sizeof(uint)),
                            uints[index]);
                    }

                    break;
                case PixelType.Half:
                    ushort[] half = new ushort[plane.Length];
                    PixelConversion.FloatToHalf(plane, half);
                    for (int index = 0; index < half.Length; index++)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            data.AsSpan(index * sizeof(ushort), sizeof(ushort)),
                            half[index]);
                    }

                    break;
            }

            return data;
        }

        private static Channel GetChannelDescription(Header header, string name)
        {
            return TryGetChannelDescription(header, name) ??
                throw new InvalidDataException($"The part does not contain channel '{name}'.");
        }

        private static Channel? TryGetChannelDescription(Header header, string name)
        {
            foreach (Channel channel in header.Channels)
            {
                if (channel.Name == name)
                {
                    return channel;
                }
            }

            return null;
        }

        private static float Lerp(float left, float right, float amount)
        {
            return left + ((right - left) * amount);
        }
    }
}
