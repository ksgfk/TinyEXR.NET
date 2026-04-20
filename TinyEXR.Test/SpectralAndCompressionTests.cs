namespace TinyEXR.Test;

[TestClass]
public sealed class SpectralAndCompressionTests
{
    [TestMethod]
    public void Spectral_channel_helpers_match_tester_cc()
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

    [TestMethod]
    public void Spectral_header_attributes_round_trip_in_memory_model()
    {
        ExrHeader header = new();

        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(header, SpectrumType.Emissive, "W.m^-2.sr^-1.nm^-1"));
        Assert.IsTrue(header.CustomAttributes.Count >= 2);
        Assert.IsTrue(header.CustomAttributes.Any(static attribute => attribute.Name == "spectralLayoutVersion" && attribute.TypeName == "string"));
        Assert.IsTrue(header.CustomAttributes.Any(static attribute => attribute.Name == "emissiveUnits" && attribute.TypeName == "string"));
        Assert.AreEqual("W.m^-2.sr^-1.nm^-1", Exr.EXRGetSpectralUnits(header));
    }

    [TestMethod]
    public void Spectral_type_detection_works_for_emissive_reflective_and_polarised()
    {
        ExrHeader emissiveHeader = new();
        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(emissiveHeader, SpectrumType.Emissive, "radiance"));
        emissiveHeader.Channels.Add(new ExrChannel("S0.400,000000nm", ExrPixelType.Float));
        emissiveHeader.Channels.Add(new ExrChannel("S0.500,000000nm", ExrPixelType.Float));
        emissiveHeader.Channels.Add(new ExrChannel("S0.600,000000nm", ExrPixelType.Float));
        Assert.AreEqual(SpectrumType.Emissive, Exr.EXRGetSpectrumType(emissiveHeader));
        float[] wavelengths = Exr.EXRGetWavelengths(emissiveHeader);
        Assert.AreEqual(3, wavelengths.Length);
        Assert.AreEqual(400.0f, wavelengths[0], 0.01f);
        Assert.AreEqual(500.0f, wavelengths[1], 0.01f);
        Assert.AreEqual(600.0f, wavelengths[2], 0.01f);

        ExrHeader reflectiveHeader = new();
        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(reflectiveHeader, SpectrumType.Reflective, "reflectance"));
        reflectiveHeader.Channels.Add(new ExrChannel("T.450,000000nm", ExrPixelType.Float));
        reflectiveHeader.Channels.Add(new ExrChannel("T.550,000000nm", ExrPixelType.Float));
        Assert.AreEqual(SpectrumType.Reflective, Exr.EXRGetSpectrumType(reflectiveHeader));

        ExrHeader polarisedHeader = new();
        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(polarisedHeader, SpectrumType.Polarised, "stokes"));
        Assert.IsTrue(polarisedHeader.CustomAttributes.Any(static attribute => attribute.Name == "polarisationHandedness"));
        polarisedHeader.Channels.Add(new ExrChannel("S0.500,000000nm", ExrPixelType.Float));
        polarisedHeader.Channels.Add(new ExrChannel("S1.500,000000nm", ExrPixelType.Float));
        polarisedHeader.Channels.Add(new ExrChannel("S2.500,000000nm", ExrPixelType.Float));
        polarisedHeader.Channels.Add(new ExrChannel("S3.500,000000nm", ExrPixelType.Float));
        Assert.AreEqual(SpectrumType.Polarised, Exr.EXRGetSpectrumType(polarisedHeader));
    }

    [TestMethod]
    public void PIZ_compression_round_trip_preserves_gradient_pixels()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
    }

    [TestMethod]
    public void PIZ_compression_handles_all_zero_images()
    {
        const int width = 16;
        const int height = 16;
        float[] zeros = new float[width * height];

        ExrImage image = RoundTrip(
            width,
            height,
            CompressionType.PIZ,
            channel => ExrPixelType.Float,
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

    [TestMethod]
    public void PIZ_large_image_round_trip_preserves_pixels()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
    }

    [TestMethod]
    public void PXR24_compression_round_trip_preserves_half_pixels()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
    }

    [TestMethod]
    public void B44_compression_round_trip_stays_within_lossy_tolerance()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.5f, "A");
    }

    [TestMethod]
    public void B44_mixed_channel_types_round_trip()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod]
    public void B44_all_float_channels_round_trip_exactly()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("B", ExrPixelType.Float, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Float, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Float, chR));

        CollectionAssert.AreEqual(chB, ExrTestHelper.ReadFloatChannel(image, "B"));
        CollectionAssert.AreEqual(chG, ExrTestHelper.ReadFloatChannel(image, "G"));
        CollectionAssert.AreEqual(chR, ExrTestHelper.ReadFloatChannel(image, "R"));
    }

    [TestMethod]
    public void B44_uint_and_half_channels_round_trip()
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

    [TestMethod]
    public void B44A_mixed_channel_types_round_trip()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod]
    public void B44_non_power_of_two_dimensions_round_trip()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Float, chA),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        CollectionAssert.AreEqual(chA, ExrTestHelper.ReadFloatChannel(image, "A"));
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
    }

    [TestMethod]
    public void B44A_flat_block_round_trip_succeeds()
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
            channel => ExrPixelType.Float,
            ExrTestHelper.FloatChannel("A", ExrPixelType.Half, chA),
            ExrTestHelper.FloatChannel("B", ExrPixelType.Half, chB),
            ExrTestHelper.FloatChannel("G", ExrPixelType.Half, chG),
            ExrTestHelper.FloatChannel("R", ExrPixelType.Half, chR));

        ExrTestHelper.AssertMaxError(chA, ExrTestHelper.ReadFloatChannel(image, "A"), 0.01f, "A");
        ExrTestHelper.AssertMaxError(chB, ExrTestHelper.ReadFloatChannel(image, "B"), 0.01f, "B");
        ExrTestHelper.AssertMaxError(chG, ExrTestHelper.ReadFloatChannel(image, "G"), 0.01f, "G");
        ExrTestHelper.AssertMaxError(chR, ExrTestHelper.ReadFloatChannel(image, "R"), 0.01f, "R");
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
}
