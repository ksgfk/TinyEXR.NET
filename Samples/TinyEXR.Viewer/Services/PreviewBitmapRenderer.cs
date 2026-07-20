using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TinyEXR.Viewer.Models;
using V3 = TinyEXR.V3;

namespace TinyEXR.Viewer.Services;

internal static class PreviewBitmapRenderer
{
    public static (PreviewBuffer? Buffer, string Message) ComposePreview(ExrPartDocument part, int levelIndex, string? layerName)
    {
        if (part.Part is null)
        {
            return (null, "Preview is unavailable for the selected part.");
        }

        if ((uint)levelIndex >= (uint)part.Part.Levels.Count)
        {
            return (null, "The selected level is out of range.");
        }

        V3.PartLevel selectedLevel = part.Part.Levels[levelIndex];
        if (selectedLevel is not V3.FlatLevel level)
        {
            return (null, "Deep levels are loaded for inspection, but a 2D preview is unavailable.");
        }

        if (level.Width > int.MaxValue || level.Height > int.MaxValue)
        {
            return (null, "The selected level is too large to preview.");
        }

        long pixelCount = checked(level.Width * level.Height);
        if (pixelCount > int.MaxValue / 4)
        {
            return (null, "The selected level is too large to preview.");
        }

        int width = (int)level.Width;
        int height = (int)level.Height;
        IReadOnlyList<LayerChannelMatch> matches = ExrLayerHelper.MatchLayer(
            part.Header.Channels,
            level,
            layerName);
        if (matches.Count == 0)
        {
            return (null, "The selected layer contains no channels in this level.");
        }

        float[] linearRgba = new float[checked((int)pixelCount * 4)];
        if (matches.Count == 1)
        {
            if (matches[0].Buffer.SampleCount == 0)
            {
                return (null, $"Channel '{matches[0].Description.Name}' has no samples in this level.");
            }

            FillSingleChannelPreview(matches[0], level.Region, width, height, linearRgba);
            return (new PreviewBuffer { Width = width, Height = height, LinearRgba = linearRgba }, "Single-channel layer previewed as grayscale.");
        }

        LayerChannelMatch? r = null;
        LayerChannelMatch? g = null;
        LayerChannelMatch? b = null;
        LayerChannelMatch? a = null;

        for (int i = 0; i < matches.Count; i++)
        {
            LayerChannelMatch match = matches[i];
            if (string.Equals(match.Name, "R", StringComparison.Ordinal))
            {
                r = match;
            }
            else if (string.Equals(match.Name, "G", StringComparison.Ordinal))
            {
                g = match;
            }
            else if (string.Equals(match.Name, "B", StringComparison.Ordinal))
            {
                b = match;
            }
            else if (string.Equals(match.Name, "A", StringComparison.Ordinal))
            {
                a = match;
            }
        }

        if (r is null || g is null || b is null)
        {
            return (null, "The selected layer is not previewable because it does not expose RGB channels.");
        }

        if (r.Buffer.SampleCount == 0 || g.Buffer.SampleCount == 0 || b.Buffer.SampleCount == 0 ||
            (a is not null && a.Buffer.SampleCount == 0))
        {
            return (null, "At least one preview channel has no samples in this level.");
        }

        FillRgbaPreview(r, g, b, a, level.Region, width, height, linearRgba);
        return (new PreviewBuffer { Width = width, Height = height, LinearRgba = linearRgba }, "Preview updated.");
    }

    public static WriteableBitmap RenderBitmap(PreviewBuffer buffer, double exposure)
    {
        byte[] pixels = new byte[checked(buffer.Width * buffer.Height * 4)];
        float exposureScale = (float)Math.Pow(2.0, exposure);

        for (int pixelIndex = 0; pixelIndex < buffer.Width * buffer.Height; pixelIndex++)
        {
            int rgbaOffset = pixelIndex * 4;
            int byteOffset = pixelIndex * 4;

            float r = buffer.LinearRgba[rgbaOffset] * exposureScale;
            float g = buffer.LinearRgba[rgbaOffset + 1] * exposureScale;
            float b = buffer.LinearRgba[rgbaOffset + 2] * exposureScale;
            float a = buffer.LinearRgba[rgbaOffset + 3];

            pixels[byteOffset] = ToSrgbByte(b);
            pixels[byteOffset + 1] = ToSrgbByte(g);
            pixels[byteOffset + 2] = ToSrgbByte(r);
            pixels[byteOffset + 3] = ToByte(Clamp01(a));
        }

        WriteableBitmap bitmap = new(
            new PixelSize(buffer.Width, buffer.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using ILockedFramebuffer locked = bitmap.Lock();
        Marshal.Copy(pixels, 0, locked.Address, pixels.Length);
        return bitmap;
    }

    private static void FillSingleChannelPreview(
        LayerChannelMatch channel,
        V3.Box2i region,
        int width,
        int height,
        Span<float> linearRgba)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = ReadChannelSampleAsFloat(channel, region, x, y);
                int offset = (y * width + x) * 4;
                linearRgba[offset] = value;
                linearRgba[offset + 1] = value;
                linearRgba[offset + 2] = value;
                linearRgba[offset + 3] = 1.0f;
            }
        }
    }

    private static void FillRgbaPreview(
        LayerChannelMatch r,
        LayerChannelMatch g,
        LayerChannelMatch b,
        LayerChannelMatch? a,
        V3.Box2i region,
        int width,
        int height,
        Span<float> linearRgba)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                linearRgba[offset] = ReadChannelSampleAsFloat(r, region, x, y);
                linearRgba[offset + 1] = ReadChannelSampleAsFloat(g, region, x, y);
                linearRgba[offset + 2] = ReadChannelSampleAsFloat(b, region, x, y);
                linearRgba[offset + 3] = a is null ? 1.0f : ReadChannelSampleAsFloat(a, region, x, y);
            }
        }
    }

    private static float ReadChannelSampleAsFloat(
        LayerChannelMatch channel,
        V3.Box2i region,
        int x,
        int y)
    {
        V3.Channel description = channel.Description;
        long firstSampleX = FloorDivide((long)region.MinX - 1L, description.XSampling) + 1L;
        long lastSampleX = FloorDivide(region.MaxX, description.XSampling);
        long firstSampleY = FloorDivide((long)region.MinY - 1L, description.YSampling) + 1L;
        long lastSampleY = FloorDivide(region.MaxY, description.YSampling);
        if (firstSampleX > lastSampleX || firstSampleY > lastSampleY)
        {
            throw new InvalidOperationException($"Channel '{description.Name}' has no samples in this level.");
        }

        long absoluteX = (long)region.MinX + x;
        long absoluteY = (long)region.MinY + y;
        long sampledX = Math.Clamp(
            FloorDivide(absoluteX, description.XSampling),
            firstSampleX,
            lastSampleX);
        long sampledY = Math.Clamp(
            FloorDivide(absoluteY, description.YSampling),
            firstSampleY,
            lastSampleY);
        long sampleWidth = lastSampleX - firstSampleX + 1L;
        int sampleIndex = checked((int)(
            (sampledY - firstSampleY) * sampleWidth + sampledX - firstSampleX));
        return ReadSampleAsFloat(channel.Buffer.Data, channel.Buffer.PixelType, sampleIndex);
    }

    private static long FloorDivide(long value, int divisor)
    {
        long quotient = value / divisor;
        if (value % divisor < 0)
        {
            quotient--;
        }

        return quotient;
    }

    private static float ReadSampleAsFloat(ReadOnlySpan<byte> data, V3.PixelType pixelType, int index)
    {
        int offset = checked(index * GetTypeSize(pixelType));
        ReadOnlySpan<byte> bytes = data.Slice(offset);
        return pixelType switch
        {
            V3.PixelType.UInt => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            V3.PixelType.Half => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
            V3.PixelType.Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
            _ => 0.0f,
        };
    }

    private static int GetTypeSize(V3.PixelType pixelType)
    {
        return pixelType switch
        {
            V3.PixelType.Half => 2,
            V3.PixelType.UInt => 4,
            V3.PixelType.Float => 4,
            _ => 0,
        };
    }

    private static byte ToSrgbByte(float value)
    {
        float clampedLinear = MathF.Max(value, 0.0f);
        float srgb = clampedLinear <= 0.0031308f
            ? clampedLinear * 12.92f
            : 1.055f * MathF.Pow(clampedLinear, 1.0f / 2.4f) - 0.055f;
        return ToByte(Clamp01(srgb));
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0.0f, 1.0f);
    }
}
