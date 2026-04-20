namespace TinyEXR.Test;

[TestClass]
public sealed class SpectralAndCompressionTests
{
    [TestMethod(DisplayName = "Spectral: Channel naming")]
    public void Case_Spectral_Channel_naming()
    {
        Assert.AreEqual("S0.550,000000nm", Exr.EXRSpectralChannelName(550.0f, 0));
        Assert.AreEqual("S1.400,500000nm", Exr.EXRSpectralChannelName(400.5f, 1));
        Assert.AreEqual("T.700,000000nm", Exr.EXRReflectiveChannelName(700.0f));

        Assert.AreEqual(550.0f, Exr.EXRParseSpectralChannelWavelength("S0.550,000000nm"), 0.01f);
        Assert.AreEqual(400.5f, Exr.EXRParseSpectralChannelWavelength("T.400,500000nm"), 0.01f);
        Assert.IsTrue(Exr.EXRParseSpectralChannelWavelength("R") < 0.0f);
        Assert.IsTrue(Exr.EXRParseSpectralChannelWavelength("InvalidChannel") < 0.0f);

        Assert.AreEqual(0, Exr.EXRGetStokesComponent("S0.550,000000nm"));
        Assert.AreEqual(1, Exr.EXRGetStokesComponent("S1.550,000000nm"));
        Assert.AreEqual(2, Exr.EXRGetStokesComponent("S2.550,000000nm"));
        Assert.AreEqual(3, Exr.EXRGetStokesComponent("S3.550,000000nm"));
        Assert.AreEqual(-1, Exr.EXRGetStokesComponent("T.550,000000nm"));
        Assert.AreEqual(-1, Exr.EXRGetStokesComponent("R"));

        Assert.IsTrue(Exr.EXRIsSpectralChannel("S0.550,000000nm"));
        Assert.IsTrue(Exr.EXRIsSpectralChannel("T.700,000000nm"));
        Assert.IsFalse(Exr.EXRIsSpectralChannel("R"));
        Assert.IsFalse(Exr.EXRIsSpectralChannel("G"));
        Assert.IsFalse(Exr.EXRIsSpectralChannel("B"));
    }

    [TestMethod(DisplayName = "Spectral: Header attributes")]
    public void Case_Spectral_Header_attributes()
    {
        ExrHeader header = new();

        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(header, SpectrumType.Emissive, "W.m^-2.sr^-1.nm^-1"));
        Assert.IsTrue(header.CustomAttributes.Count >= 2);
        Assert.IsTrue(header.CustomAttributes.Any(static attribute => attribute.Name == "spectralLayoutVersion" && attribute.TypeName == "string"));
        Assert.IsTrue(header.CustomAttributes.Any(static attribute => attribute.Name == "emissiveUnits" && attribute.TypeName == "string"));
        Assert.AreEqual("W.m^-2.sr^-1.nm^-1", Exr.EXRGetSpectralUnits(header));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] Spectral: IsSpectralEXR only requires spectralLayoutVersion")]
    public void Case_Spectral_IsSpectralEXR_only_requires_spectralLayoutVersion()
    {
        ExrImage image = new(
            1,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel("B", ExrPixelType.Float, new[] { 0.25f }),
                ExrTestHelper.FloatChannel("G", ExrPixelType.Float, new[] { 0.5f }),
                ExrTestHelper.FloatChannel("R", ExrPixelType.Float, new[] { 1.0f }),
            });

        ExrHeader header = new();
        header.CustomAttributes.Add(ExrAttribute.FromString("spectralLayoutVersion", "1.0"));

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.IsTrue(Exr.IsSpectralEXRFromMemory(encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
        Assert.IsTrue(decodedHeader.CustomAttributes.Any(static attribute => attribute.Name == "spectralLayoutVersion"));
        Assert.IsNull(Exr.EXRGetSpectrumType(decodedHeader));

        string path = Path.Combine(Path.GetTempPath(), $"spectral-layout-version-rgb-{Guid.NewGuid():N}.exr");
        try
        {
            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToFile(image, header, path));
            Assert.IsTrue(Exr.IsSpectralEXR(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod(DisplayName = "Spectral: Spectrum type detection")]
    public void Case_Spectral_Spectrum_type_detection()
    {
        ExrHeader header = new();

        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(header, SpectrumType.Emissive, "radiance"));
        header.Channels.Add(new ExrChannel("S0.400,000000nm", ExrPixelType.Float));
        header.Channels.Add(new ExrChannel("S0.500,000000nm", ExrPixelType.Float));
        header.Channels.Add(new ExrChannel("S0.600,000000nm", ExrPixelType.Float));

        Assert.AreEqual(SpectrumType.Emissive, Exr.EXRGetSpectrumType(header));

        float[] wavelengths = Exr.EXRGetWavelengths(header);
        Assert.AreEqual(3, wavelengths.Length);
        Assert.AreEqual(400.0f, wavelengths[0], 0.01f);
        Assert.AreEqual(500.0f, wavelengths[1], 0.01f);
        Assert.AreEqual(600.0f, wavelengths[2], 0.01f);
    }

    [TestMethod(DisplayName = "Spectral: Reflective spectrum type")]
    public void Case_Spectral_Reflective_spectrum_type()
    {
        ExrHeader header = new();

        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(header, SpectrumType.Reflective, "reflectance"));
        header.Channels.Add(new ExrChannel("T.450,000000nm", ExrPixelType.Float));
        header.Channels.Add(new ExrChannel("T.550,000000nm", ExrPixelType.Float));

        Assert.AreEqual(SpectrumType.Reflective, Exr.EXRGetSpectrumType(header));
    }

    [TestMethod(DisplayName = "Spectral: Polarised spectrum type")]
    public void Case_Spectral_Polarised_spectrum_type()
    {
        ExrHeader header = new();

        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(header, SpectrumType.Polarised, "stokes"));
        Assert.IsTrue(header.CustomAttributes.Any(static attribute => attribute.Name == "polarisationHandedness"));
        header.Channels.Add(new ExrChannel("S0.500,000000nm", ExrPixelType.Float));
        header.Channels.Add(new ExrChannel("S1.500,000000nm", ExrPixelType.Float));
        header.Channels.Add(new ExrChannel("S2.500,000000nm", ExrPixelType.Float));
        header.Channels.Add(new ExrChannel("S3.500,000000nm", ExrPixelType.Float));

        Assert.AreEqual(SpectrumType.Polarised, Exr.EXRGetSpectrumType(header));
    }

    [TestMethod(DisplayName = "PIZ: Compression round-trip")]
    public void Case_PIZ_Compression_round_trip()
    {
        const int width = 64;
        const int height = 64;

        float[] chR = new float[width * height];
        float[] chG = new float[width * height];
        float[] chB = new float[width * height];
        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                chR[index] = (float)x / width;
                chG[index] = (float)y / height;
                chB[index] = 0.5f + 0.1f * MathF.Sin(x * 0.1f);
            }
        }

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.PIZ,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
    }

    [TestMethod(DisplayName = "PIZ: Compression all zeros (issue 194)")]
    public void Case_PIZ_Compression_all_zeros_issue_194()
    {
        const int width = 16;
        const int height = 16;
        float[] zeros = new float[width * height];

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.PIZ,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, zeros),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, zeros),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, zeros),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, zeros));

        foreach (ExrImageChannel channel in image.Channels)
        {
            float[] samples = ExrTestHelper.ReadFloatChannel(channel);
            foreach (float value in samples)
            {
                Assert.AreEqual(0.0f, value, 0.0f);
            }
        }
    }

    [TestMethod(DisplayName = "PIZ: Large image compression")]
    public void Case_PIZ_Large_image_compression()
    {
        const int width = 256;
        const int height = 256;

        float[] chR = new float[width * height];
        float[] chG = new float[width * height];
        float[] chB = new float[width * height];
        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                chR[index] = (float)x / width;
                chG[index] = (float)y / height;
                chB[index] = MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f) * 0.5f + 0.5f;
            }
        }

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.PIZ,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
    }

    [TestMethod(DisplayName = "PXR24: Compression round-trip")]
    public void Case_PXR24_Compression_round_trip()
    {
        const int width = 32;
        const int height = 32;

        float[] chR = new float[width * height];
        float[] chG = new float[width * height];
        float[] chB = Enumerable.Repeat(0.5f, width * height).ToArray();
        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                chR[index] = (float)x / width;
                chG[index] = (float)y / height;
            }
        }

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.PXR24,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
    }

    [TestMethod(DisplayName = "B44: Compression round-trip")]
    public void Case_B44_Compression_round_trip()
    {
        const int width = 32;
        const int height = 32;

        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();
        float[] chB = Enumerable.Repeat(0.5f, width * height).ToArray();
        float[] chG = Enumerable.Repeat(0.5f, width * height).ToArray();
        float[] chR = Enumerable.Repeat(0.5f, width * height).ToArray();

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.5f, "A");
    }

    [TestMethod(DisplayName = "Regression: B44 mixed channel types (issue 239)")]
    public void Case_Regression_B44_mixed_channel_types_issue_239()
    {
        const int width = 32;
        const int height = 32;

        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();
        float[] chG = Enumerable.Repeat(0.25f, width * height).ToArray();
        float[] chR = Enumerable.Repeat(0.5f, width * height).ToArray();

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod(DisplayName = "Regression: B44 all-FLOAT channels (issue 239 variant)")]
    public void Case_Regression_B44_all_FLOAT_channels_issue_239_variant()
    {
        const int width = 32;
        const int height = 32;

        float[] chB = new float[width * height];
        float[] chG = new float[width * height];
        float[] chR = new float[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                chB[index] = (float)x / width;
                chG[index] = (float)y / height;
                chR[index] = 0.75f;
            }
        }

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("B", ExrPixelType.Float, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Float, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Float, chR));

        CollectionAssert.AreEqual(chB, ExrTestHelper.ReadFloatChannel(image, "B"));
        CollectionAssert.AreEqual(chG, ExrTestHelper.ReadFloatChannel(image, "G"));
        CollectionAssert.AreEqual(chR, ExrTestHelper.ReadFloatChannel(image, "R"));
    }

    [TestMethod(DisplayName = "Regression: B44 UINT+HALF mixed channels (issue 239 variant)")]
    public void Case_Regression_B44_UINT_HALF_mixed_channels_issue_239_variant()
    {
        const int width = 16;
        const int height = 16;

        uint[] chA = Enumerable.Range(0, width * height).Select(static i => (uint)i).ToArray();
        float[] chB = Enumerable.Repeat(0.5f, width * height).ToArray();

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44,
            static channel => channel.Type == ExrPixelType.UInt ? ExrPixelType.UInt : ExrPixelType.Float,
            ExrTestHelper.UIntChannel("A", ExrPixelType.UInt, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadUIntChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
    }

    [TestMethod(DisplayName = "Regression: B44A mixed channel types (issue 239 variant)")]
    public void Case_Regression_B44A_mixed_channel_types_issue_239_variant()
    {
        const int width = 32;
        const int height = 32;

        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();
        float[] chG = Enumerable.Repeat(0.25f, width * height).ToArray();
        float[] chR = Enumerable.Repeat(0.5f, width * height).ToArray();

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44A,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod(DisplayName = "Regression: B44 non-power-of-2 dimensions (issue 239)")]
    public void Case_Regression_B44_non_power_of_2_dimensions_issue_239()
    {
        const int width = 13;
        const int height = 7;

        float[] chA = new float[width * height];
        float[] chG = Enumerable.Repeat(0.25f, width * height).ToArray();
        float[] chR = Enumerable.Repeat(0.5f, width * height).ToArray();

        for (int i = 0; i < chA.Length; i++)
        {
            chA[i] = i * 0.01f;
        }

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod(DisplayName = "B44A: Flat block compression")]
    public void Case_B44A_Flat_block_compression()
    {
        const int width = 16;
        const int height = 16;

        float[] chA = Enumerable.Repeat(1.0f, width * height).ToArray();
        float[] chB = new float[width * height];
        float[] chG = Enumerable.Repeat(0.5f, width * height).ToArray();
        float[] chR = Enumerable.Repeat(0.25f, width * height).ToArray();

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.B44A,
            static channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] B44 subsampled channels round-trip")]
    public void Case_B44_Subsampled_channels_round_trip()
    {
        AssertSubsampledB44RoundTrip(CompressionType.B44);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] B44A subsampled channels round-trip")]
    public void Case_B44A_Subsampled_channels_round_trip()
    {
        AssertSubsampledB44RoundTrip(CompressionType.B44A);
    }

    private static ExrImage RoundTrip(
        int width,
        int height,
        CompressionType compression,
        Func<ExrChannel, ExrPixelType> requestedTypeSelector,
        params ExrImageChannel[] channels)
    {
        ExrImage sourceImage = new(width, height, channels);
        ExrHeader sourceHeader = new()
        {
            Compression = compression,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(sourceImage, sourceHeader, out byte[] encoded));
        Assert.IsTrue(encoded.Length > 0);
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out _));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
        ExrTestHelper.SetRequestedPixelTypes(decodedHeader, requestedTypeSelector);
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));
        return decodedImage;
    }

    private static void AssertSubsampledB44RoundTrip(CompressionType compression)
    {
        const int width = 13;
        const int height = 11;
        const int samplingX = 2;
        const int samplingY = 2;

        float[] chA = new float[width * height];
        float[] chR = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                chA[index] = x * 0.0625f + y * 0.015625f;
                chR[index] = 1.0f - x * 0.03125f + y * 0.0078125f;
            }
        }

        int sampledWidth = (width + samplingX - 1) / samplingX;
        int sampledHeight = (height + samplingY - 1) / samplingY;
        float[] chB = Enumerable.Repeat(0.25f, sampledWidth * sampledHeight).ToArray();
        float[] chG = new float[sampledWidth * sampledHeight];
        for (int sampleY = 0; sampleY < sampledHeight; sampleY++)
        {
            for (int sampleX = 0; sampleX < sampledWidth; sampleX++)
            {
                int index = sampleY * sampledWidth + sampleX;
                chG[index] = 0.125f + sampleX * 0.03125f + sampleY * 0.0625f;
            }
        }

        ExrImage image = RoundTrip(
            width,
            height,
            compression,
            static _ => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            new ExrImageChannel(
                new ExrChannel("B", ExrPixelType.Half, samplingX, samplingY, 1),
                ExrPixelType.Float,
                ExrTestHelper.ToBytes(chB)),
            new ExrImageChannel(
                new ExrChannel("G", ExrPixelType.Half, samplingX, samplingY, 1),
                ExrPixelType.Float,
                ExrTestHelper.ToBytes(chG)),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Float, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.02f, "B");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.02f, "G");
        CollectionAssert.AreEqual(chR, ExrTestHelper.ReadFloatChannel(image, "R"));
    }
}
