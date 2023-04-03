using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using TinyEXR;

{//test reader
    SinglePartExrReader reader = new();
    reader.Read("table_mountain_2_puresky_1k.exr");

    using Image<Rgba32> image = new(reader.Width, reader.Height, new Rgba32(0, 0, 0, 255));
    image.ProcessPixelRows(accessor =>
    {
        var r = reader.GetImageData("R");
        var g = reader.GetImageData("G");
        var b = reader.GetImageData("B");

        for (int y = 0; y < accessor.Height; y++)
        {
            Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
            for (int x = 0; x < pixelRow.Length; x++)
            {
                ref Rgba32 pixel = ref pixelRow[x];
                int idx = (x + y * reader.Width) * 4;
                float m = MemoryMarshal.Cast<byte, float>(r.Slice(idx, 4))[0];
                float n = MemoryMarshal.Cast<byte, float>(g.Slice(idx, 4))[0];
                float t = MemoryMarshal.Cast<byte, float>(b.Slice(idx, 4))[0];
                pixel.R = (byte)(MathF.Min(ToSrgb(m), 1.0f) * byte.MaxValue);
                pixel.G = (byte)(MathF.Min(ToSrgb(n), 1.0f) * byte.MaxValue);
                pixel.B = (byte)(MathF.Min(ToSrgb(t), 1.0f) * byte.MaxValue);
                pixel.A = 255;
            }
        }
    });
    image.SaveAsPng("test.png");
    Console.WriteLine("exr to png done.");
}

{//test writer
    using Image<Rgba32> image = Image.Load<Rgba32>("100639413_p0.png");
    float[] r = new float[image.Width * image.Height];
    float[] g = new float[image.Width * image.Height];
    float[] b = new float[image.Width * image.Height];
    image.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
            for (int x = 0; x < pixelRow.Length; x++)
            {
                ref Rgba32 pixel = ref pixelRow[x];
                int idx = (x + y * image.Width);
                r[idx] = MathF.Min(MathF.Max(ToLinear((float)pixel.R / byte.MaxValue), 0), 1);
                g[idx] = MathF.Min(MathF.Max(ToLinear((float)pixel.G / byte.MaxValue), 0), 1);
                b[idx] = MathF.Min(MathF.Max(ToLinear((float)pixel.B / byte.MaxValue), 0), 1);
            }
        }
    });
    ScanlineExrWriter writer = new ScanlineExrWriter()
        .AddChannel("B", ExrPixelType.Float, MemoryMarshal.AsBytes<float>(b).ToArray(), ExrPixelType.Float)
        .AddChannel("G", ExrPixelType.Float, MemoryMarshal.AsBytes<float>(g).ToArray(), ExrPixelType.Float)
        .AddChannel("R", ExrPixelType.Float, MemoryMarshal.AsBytes<float>(r).ToArray(), ExrPixelType.Float)
        .SetSize(image.Width, image.Height);
    writer.Save("wife.exr");
    Console.WriteLine("png to exr done.");
}

static float ToSrgb(float val)
{
    if (val > 0.0031308f)
    {
        val = 1.055f * (MathF.Pow(val, (1.0f / 2.4f))) - 0.055f;
    }
    else
    {
        val = 12.92f * val;
    }
    return val;
}

static float ToLinear(float val)
{
    if (val > 0.040448f)
    {
        val = MathF.Pow((val + 0.055f) / 1.055f, 2.4f);
    }
    else
    {
        val = val / 12.92f;
    }
    return val;
}
