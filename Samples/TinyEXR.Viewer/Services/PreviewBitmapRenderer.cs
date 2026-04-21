using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TinyEXR;
using TinyEXR.Viewer.Models;

namespace TinyEXR.Viewer.Services;

internal static class PreviewBitmapRenderer
{
    public static (PreviewBuffer? Buffer, string Message) ComposePreview(ExrPartDocument part, int levelIndex, string? layerName)
    {
        if (part.Image is null)
        {
            return (null, "Preview is unavailable for the selected part.");
        }

        if ((uint)levelIndex >= (uint)part.Image.Levels.Count)
        {
            return (null, "The selected level is out of range.");
        }

        ExrImageLevel level = part.Image.Levels[levelIndex];
        IReadOnlyList<LayerChannelMatch> matches = ExrLayerHelper.MatchLayer(level.Channels, layerName);
        if (matches.Count == 0)
        {
            return (null, "The selected layer contains no channels in this level.");
        }

        float[] linearRgba = new float[checked(level.Width * level.Height * 4)];
        if (matches.Count == 1)
        {
            FillSingleChannelPreview(matches[0].Channel, level.Width, level.Height, linearRgba);
            return (new PreviewBuffer { Width = level.Width, Height = level.Height, LinearRgba = linearRgba }, "Single-channel layer previewed as grayscale.");
        }

        ExrImageChannel? r = null;
        ExrImageChannel? g = null;
        ExrImageChannel? b = null;
        ExrImageChannel? a = null;

        for (int i = 0; i < matches.Count; i++)
        {
            LayerChannelMatch match = matches[i];
            if (string.Equals(match.Name, "R", StringComparison.Ordinal))
            {
                r = match.Channel;
            }
            else if (string.Equals(match.Name, "G", StringComparison.Ordinal))
            {
                g = match.Channel;
            }
            else if (string.Equals(match.Name, "B", StringComparison.Ordinal))
            {
                b = match.Channel;
            }
            else if (string.Equals(match.Name, "A", StringComparison.Ordinal))
            {
                a = match.Channel;
            }
        }

        if (r is null || g is null || b is null)
        {
            return (null, "The selected layer is not previewable because it does not expose RGB channels.");
        }

        FillRgbaPreview(r, g, b, a, level.Width, level.Height, linearRgba);
        return (new PreviewBuffer { Width = level.Width, Height = level.Height, LinearRgba = linearRgba }, "Preview updated.");
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

    private static void FillSingleChannelPreview(ExrImageChannel channel, int width, int height, Span<float> linearRgba)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = ReadChannelSampleAsFloat(channel, width, x, y);
                int offset = (y * width + x) * 4;
                linearRgba[offset] = value;
                linearRgba[offset + 1] = value;
                linearRgba[offset + 2] = value;
                linearRgba[offset + 3] = 1.0f;
            }
        }
    }

    private static void FillRgbaPreview(
        ExrImageChannel r,
        ExrImageChannel g,
        ExrImageChannel b,
        ExrImageChannel? a,
        int width,
        int height,
        Span<float> linearRgba)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                linearRgba[offset] = ReadChannelSampleAsFloat(r, width, x, y);
                linearRgba[offset + 1] = ReadChannelSampleAsFloat(g, width, x, y);
                linearRgba[offset + 2] = ReadChannelSampleAsFloat(b, width, x, y);
                linearRgba[offset + 3] = a is null ? 1.0f : ReadChannelSampleAsFloat(a, width, x, y);
            }
        }
    }

    private static float ReadChannelSampleAsFloat(ExrImageChannel channel, int imageWidth, int x, int y)
    {
        int sampleWidth = CountSamplePositions(0, imageWidth, channel.Channel.SamplingX);
        int sampleX = CountSamplePositions(0, x + 1, channel.Channel.SamplingX) - 1;
        int sampleY = CountSamplePositions(0, y + 1, channel.Channel.SamplingY) - 1;
        int sampleIndex = checked(sampleY * sampleWidth + sampleX);
        return ReadSampleAsFloat(channel.Data, channel.DataType, sampleIndex);
    }

    private static int CountSamplePositions(int start, int size, int sampling)
    {
        if (size <= 0)
        {
            return 0;
        }

        int remainder = start % sampling;
        if (remainder < 0)
        {
            remainder += sampling;
        }

        int firstOffset = remainder == 0 ? 0 : sampling - remainder;
        if (firstOffset >= size)
        {
            return 0;
        }

        return ((size - 1 - firstOffset) / sampling) + 1;
    }

    private static float ReadSampleAsFloat(byte[] data, ExrPixelType pixelType, int index)
    {
        int offset = checked(index * GetTypeSize(pixelType));
        ReadOnlySpan<byte> bytes = data.AsSpan(offset);
        return pixelType switch
        {
            ExrPixelType.UInt => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            ExrPixelType.Half => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
            ExrPixelType.Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
            _ => 0.0f,
        };
    }

    private static int GetTypeSize(ExrPixelType pixelType)
    {
        return pixelType switch
        {
            ExrPixelType.Half => 2,
            ExrPixelType.UInt => 4,
            ExrPixelType.Float => 4,
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
