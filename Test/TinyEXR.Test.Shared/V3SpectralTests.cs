using System.Buffers.Binary;
using V3 = TinyEXR.V3;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3SpectralTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 spectral channel helpers match the JCGT layout")]
    public void Case_V3Spectral_ChannelHelpersMatchJcgtLayout()
    {
        Assert.AreEqual(
            "S0.550,000000nm",
            V3.Spectral.GetChannelName(V3.SpectrumType.Emissive, 550.0f));
        Assert.AreEqual(
            "S3.400,500000nm",
            V3.Spectral.GetChannelName(V3.SpectrumType.Polarised, 400.5f, 9));
        Assert.AreEqual(
            "T.700,000000nm",
            V3.Spectral.GetChannelName(V3.SpectrumType.Reflective, 700.0f));

        Assert.IsTrue(V3.Spectral.TryParseChannelWavelength("S2.480,250000nm", out float wavelength));
        Assert.AreEqual(480.25f, wavelength, 0.0001f);
        Assert.IsTrue(V3.Spectral.TryGetStokesComponent("S2.480,250000nm", out int stokes));
        Assert.AreEqual(2, stokes);
        Assert.IsFalse(V3.Spectral.TryGetStokesComponent("T.480,250000nm", out _));
        Assert.IsTrue(V3.Spectral.IsSpectralChannel("T.480,250000nm"));
        Assert.IsFalse(V3.Spectral.IsSpectralChannel("R"));
        Assert.IsFalse(V3.Spectral.IsSpectralChannel("S0.550,0nm.trailing"));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 emissive spectral cube round-trips through ZSTD memory and file reads")]
    public void Case_V3Spectral_EmissiveCubeRoundTripsThroughMemoryAndFile()
    {
        const int width = 3;
        const int height = 2;
        float[] sourceWavelengths = { 700.0f, 480.0f, 620.0f, 550.0f };
        float[] samples = new float[sourceWavelengths.Length * width * height];
        for (int wavelengthIndex = 0; wavelengthIndex < sourceWavelengths.Length; wavelengthIndex++)
        {
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                samples[(wavelengthIndex * width * height) + pixel] =
                    (wavelengthIndex * 100.0f) + (pixel * 0.5f);
            }
        }

        V3.Part part = V3.Spectral.CreateEmissivePart(
            width,
            height,
            sourceWavelengths,
            samples,
            "W.m^-2.sr^-1",
            V3.Compression.ZSTD);
        Assert.IsTrue(part.IsComplete);
        Assert.IsTrue(V3.Spectral.IsSpectral(part.Header));
        Assert.AreEqual(V3.SpectrumType.Emissive, V3.Spectral.GetSpectrumType(part.Header));
        Assert.AreEqual("W.m^-2.sr^-1", V3.Spectral.GetUnits(part.Header));

        byte[] encoded = EncodeScanlinePart(part);
        V3.ReaderResult<V3.SpectralImage> memoryResult = V3.SpectralImage.LoadFromMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, memoryResult.Status, memoryResult.Error?.ToString());
        Assert.IsNotNull(memoryResult.Value);
        AssertSpectralCube(memoryResult.Value, sourceWavelengths, samples);

        string path = Path.Combine(Path.GetTempPath(), $"TinyEXRNet-V3Spectral-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);
            V3.ReaderResult<V3.SpectralImage> fileResult = V3.SpectralImage.LoadFromFile(path);
            Assert.AreEqual(V3.ExrResult.Success, fileResult.Status, fileResult.Error?.ToString());
            Assert.IsNotNull(fileResult.Value);
            AssertSpectralCube(fileResult.Value, sourceWavelengths, samples);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 spectral cube point-expands sampled Half channels")]
    public void Case_V3Spectral_PointExpandsSampledHalfChannels()
    {
        const string channelName = "S0.550,000000nm";
        V3.Box2i dataWindow = new(-4, 6, -1, 9);
        V3.Channel channel = new(channelName, V3.PixelType.Half, 2, 2);
        V3.Header header = new(
            V3.PartType.Scanline,
            dataWindow,
            new[] { channel },
            compression: V3.Compression.ZIP);
        header = V3.Spectral.WithSpectralAttributes(header, V3.SpectrumType.Emissive, "radiance");

        ushort[] half = { 0x4900, 0x4d00, 0x4f80, 0x5100 };
        byte[] data = new byte[half.Length * sizeof(ushort)];
        for (int index = 0; index < half.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(index * sizeof(ushort), sizeof(ushort)),
                half[index]);
        }

        V3.Part part = new(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    dataWindow,
                    new[] { new V3.ChannelBuffer(channelName, V3.PixelType.Half, data) }),
            },
            isComplete: true);
        byte[] encoded = EncodeScanlinePart(part);
        V3.ReaderResult<V3.SpectralImage> result = V3.SpectralImage.LoadFromMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        Assert.IsNotNull(result.Value);
        V3.SpectralImage image = result.Value;
        Assert.AreEqual(4, image.Width);
        Assert.AreEqual(4, image.Height);
        CollectionAssert.AreEqual(new[] { 550.0f }, image.Wavelengths.ToArray());
        float[] expected =
        {
            10.0f, 10.0f, 20.0f, 20.0f,
            10.0f, 10.0f, 20.0f, 20.0f,
            30.0f, 30.0f, 40.0f, 40.0f,
            30.0f, 30.0f, 40.0f, 40.0f,
        };
        CollectionAssert.AreEqual(expected, image.GetStokesPlane(0).ToArray());
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 spectral probes distinguish marker and channel classification")]
    public void Case_V3Spectral_DistinguishesMarkerAndChannelClassification()
    {
        V3.Header rgb = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 0, 0),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            attributes: new[]
            {
                new V3.HeaderAttribute(
                    "spectralLayoutVersion",
                    "string",
                    System.Text.Encoding.UTF8.GetBytes("1.0")),
            });
        Assert.IsTrue(V3.Spectral.IsSpectral(rgb));
        Assert.AreEqual(V3.SpectrumType.None, V3.Spectral.GetSpectrumType(rgb));

        V3.Part nonSpectral = new(
            new V3.Header(
                V3.PartType.Scanline,
                new V3.Box2i(0, 0, 0, 0),
                new[] { new V3.Channel("R", V3.PixelType.Float) }),
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    new V3.Box2i(0, 0, 0, 0),
                    new[] { new V3.ChannelBuffer("R", V3.PixelType.Float, new byte[4]) }),
            },
            isComplete: true);
        byte[] encoded = EncodeScanlinePart(nonSpectral);
        V3.ReaderResult<V3.SpectralImage> result = V3.SpectralImage.LoadFromMemory(encoded);
        Assert.AreEqual(V3.ExrResult.Unsupported, result.Status);
        Assert.IsNull(result.Value);
    }

    private static void AssertSpectralCube(
        V3.SpectralImage image,
        IReadOnlyList<float> sourceWavelengths,
        IReadOnlyList<float> sourceSamples)
    {
        Assert.AreEqual(3, image.Width);
        Assert.AreEqual(2, image.Height);
        Assert.AreEqual(V3.SpectrumType.Emissive, image.SpectrumType);
        Assert.AreEqual("W.m^-2.sr^-1", image.Units);
        CollectionAssert.AreEqual(
            sourceWavelengths.OrderBy(static wavelength => wavelength).ToArray(),
            image.Wavelengths.ToArray());

        int pixelCount = image.Width * image.Height;
        for (int sortedIndex = 0; sortedIndex < image.WavelengthCount; sortedIndex++)
        {
            float wavelength = image.Wavelengths[sortedIndex];
            int sourceIndex = sourceWavelengths
                .Select(static (value, index) => (value, index))
                .Single(item => item.value == wavelength)
                .index;
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                Assert.AreEqual(
                    sourceSamples[(sourceIndex * pixelCount) + pixel],
                    image.GetSample(0, sortedIndex, pixel % image.Width, pixel / image.Width));
            }
        }

        float[] spectrum = new float[image.WavelengthCount];
        Assert.AreEqual(
            image.WavelengthCount,
            image.CopyPixelSpectrum(0, 1, 0, spectrum));
        for (int wavelengthIndex = 0; wavelengthIndex < spectrum.Length; wavelengthIndex++)
        {
            Assert.AreEqual(image.GetSample(0, wavelengthIndex, 1, 0), spectrum[wavelengthIndex]);
        }
    }

    private static byte[] EncodeScanlinePart(V3.Part part)
    {
        Assert.AreEqual(V3.PartType.Scanline, part.Header.PartType);
        V3.FlatLevel level = (V3.FlatLevel)part.GetLevel(0, 0);
        using MemoryStream stream = new();
        using V3IO.StreamDataSink sink = new(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        Assert.AreEqual(0, writer.AddPart(part.Header));
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = writer.GetBlockInfo(0, blockIndex);
            List<V3.ChannelBuffer> channels = new(part.Header.Channels.Count);
            foreach (V3.Channel channel in part.Header.Channels)
            {
                V3.ChannelBuffer source = level.GetChannel(channel.Name);
                int elementSize = channel.PixelType == V3.PixelType.Half ? 2 : 4;
                int sampledWidth = CountSamples(
                    part.Header.DataWindow.MinX,
                    part.Header.DataWindow.MaxX,
                    channel.XSampling);
                int blockSampleCount = checked(
                    CountSamples(block.Region.MinX, block.Region.MaxX, channel.XSampling) *
                    CountSamples(block.Region.MinY, block.Region.MaxY, channel.YSampling));
                byte[] data = new byte[checked(blockSampleCount * elementSize)];
                int targetOffset = 0;
                for (int y = block.Region.MinY; y <= block.Region.MaxY; y++)
                {
                    if (y % channel.YSampling != 0)
                    {
                        continue;
                    }

                    int sampledY = CountSamples(
                        part.Header.DataWindow.MinY,
                        y,
                        channel.YSampling) - 1;
                    for (int x = block.Region.MinX; x <= block.Region.MaxX; x++)
                    {
                        if (x % channel.XSampling != 0)
                        {
                            continue;
                        }

                        int sampledX = CountSamples(
                            part.Header.DataWindow.MinX,
                            x,
                            channel.XSampling) - 1;
                        int sourceOffset = checked(((sampledY * sampledWidth) + sampledX) * elementSize);
                        source.Data.Slice(sourceOffset, elementSize).CopyTo(
                            data.AsSpan(targetOffset, elementSize));
                        targetOffset += elementSize;
                    }
                }

                Assert.AreEqual(data.Length, targetOffset);
                channels.Add(new V3.ChannelBuffer(channel.Name, channel.PixelType, data));
            }

            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteScanlineBlock(0, block.Region.MinY, channels).Status);
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static int CountSamples(int minimum, int maximum, int sampling)
    {
        return checked(FloorDivide(maximum, sampling) - FloorDivide(minimum - 1, sampling));
    }

    private static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }
}
