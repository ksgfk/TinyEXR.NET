using System.Buffers.Binary;
using V3 = TinyEXR;

namespace TinyEXR.Viewer.Services;

internal static class ExrChannelSampler
{
    public static ChannelSample? Read(LayerChannelMatch channel, V3.Box2i region, int x, int y)
    {
        V3.Channel description = channel.Description;
        long firstSampleX = FloorDivide((long)region.MinX - 1L, description.XSampling) + 1L;
        long lastSampleX = FloorDivide(region.MaxX, description.XSampling);
        long firstSampleY = FloorDivide((long)region.MinY - 1L, description.YSampling) + 1L;
        long lastSampleY = FloorDivide(region.MaxY, description.YSampling);
        if (firstSampleX > lastSampleX || firstSampleY > lastSampleY || channel.Buffer.SampleCount == 0)
        {
            return null;
        }

        // Use the same held sample as the preview for subsampled channels.
        long sampledX = Math.Clamp(
            FloorDivide((long)region.MinX + x, description.XSampling), firstSampleX, lastSampleX);
        long sampledY = Math.Clamp(
            FloorDivide((long)region.MinY + y, description.YSampling), firstSampleY, lastSampleY);
        long sampleWidth = lastSampleX - firstSampleX + 1L;
        int sampleIndex = checked((int)((sampledY - firstSampleY) * sampleWidth + sampledX - firstSampleX));
        V3.PixelType pixelType = channel.Buffer.PixelType;
        int typeSize = pixelType == V3.PixelType.Half ? 2 : 4;
        ReadOnlySpan<byte> bytes = channel.Buffer.Data.Slice(checked(sampleIndex * typeSize), typeSize);
        // Double preserves both Float and all 32-bit UInt values without rounding.
        double value = pixelType switch
        {
            V3.PixelType.UInt => (double)BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            V3.PixelType.Half => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
            V3.PixelType.Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
            _ => throw new InvalidOperationException($"Unsupported pixel type: {pixelType}."),
        };

        return new ChannelSample(value,
            checked((int)(sampledX * description.XSampling)),
            checked((int)(sampledY * description.YSampling)));
    }

    private static long FloorDivide(long value, int divisor)
    {
        long quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }
}

internal readonly record struct ChannelSample(double Value, int X, int Y);
