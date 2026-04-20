namespace TinyEXR.Test;

[TestClass]
public sealed class MultipartCompatibilityTests
{
    private const string UnknownPartType = "mysteryimageX";

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultipartCompatibility|HeterogeneousParts|OrderAndMetadata")]
    public void Case_MultipartCompatibility_HeterogeneousParts_OrderAndMetadata()
    {
        MultipartFixture fixture = CreateFixture();

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(fixture.Images, fixture.Headers, out byte[] encoded));
        AssertMultipartFixture(encoded, fixture);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultipartCompatibility|UnknownPartType|HeaderReadableAndLoadable")]
    public void Case_MultipartCompatibility_UnknownPartType_HeaderReadableAndLoadable()
    {
        MultipartFixture fixture = CreateFixture();

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(fixture.Images, fixture.Headers, out byte[] encoded));
        ExrBinaryMutationHelper.ReplaceHeaderCStringAttributeValue(encoded, headerIndex: 0, "type", "string", UnknownPartType);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version));
        Assert.IsTrue(version.Multipart);
        Assert.IsFalse(version.NonImage);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(UnknownPartType, headers.Headers[0].PartType);

        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, headers, out ExrMultipartImage images));
        AssertMultipartFixture(fixture, version, headers, images, firstPartTypeOverride: UnknownPartType);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] MultipartCompatibility|MultipartNonImageFlag|HeaderReadableImagesRejected")]
    public void Case_MultipartCompatibility_MultipartNonImageFlag_HeaderReadableImagesRejected()
    {
        MultipartFixture fixture = CreateFixture();

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(fixture.Images, fixture.Headers, out byte[] encoded));
        encoded[5] = (byte)(encoded[5] | 0x8);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version));
        Assert.IsTrue(version.Multipart);
        Assert.IsTrue(version.NonImage);

        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(fixture.Parts.Length, headers.Headers.Count);
        Assert.IsTrue(headers.Headers.All(static header => header.IsDeep));
        Assert.AreEqual("scanlineimage", headers.Headers[0].PartType);

        Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.LoadEXRMultipartImageFromMemory(encoded, headers, out ExrMultipartImage images));
        Assert.AreEqual(0, images.Images.Count);
    }

    private static MultipartFixture CreateFixture()
    {
        ExpectedPart[] parts =
        {
            new(
                Name: "scan-rle-rgb",
                Compression: CompressionType.RLE,
                IsTiled: false,
                Width: 2,
                Height: 2,
                ExpectedTileCount: 0,
                Header: new ExrHeader
                {
                    Name = "scan-rle-rgb",
                    Compression = CompressionType.RLE,
                },
                Image: new ExrImage(
                    2,
                    2,
                    new[]
                    {
                        ExrTestHelper.FloatChannel("R", ExrPixelType.Float, new[] { 1.0f, 2.0f, 3.0f, 4.0f }),
                        ExrTestHelper.FloatChannel("G", ExrPixelType.Float, new[] { 5.0f, 6.0f, 7.0f, 8.0f }),
                        ExrTestHelper.FloatChannel("B", ExrPixelType.Float, new[] { 9.0f, 10.0f, 11.0f, 12.0f }),
                    }),
                Channels:
                [
                    ExpectedChannel.Float("R", new[] { 1.0f, 2.0f, 3.0f, 4.0f }),
                    ExpectedChannel.Float("G", new[] { 5.0f, 6.0f, 7.0f, 8.0f }),
                    ExpectedChannel.Float("B", new[] { 9.0f, 10.0f, 11.0f, 12.0f }),
                ]),
            new(
                Name: "tile-zip-y",
                Compression: CompressionType.ZIP,
                IsTiled: true,
                Width: 4,
                Height: 4,
                ExpectedTileCount: 4,
                Header: new ExrHeader
                {
                    Name = "tile-zip-y",
                    Compression = CompressionType.ZIP,
                    Tiles = new ExrTileDescription
                    {
                        TileSizeX = 2,
                        TileSizeY = 2,
                        LevelMode = ExrTileLevelMode.OneLevel,
                        RoundingMode = ExrTileRoundingMode.RoundDown,
                    },
                },
                Image: new ExrImage(
                    4,
                    4,
                    new[]
                    {
                        ExrTestHelper.FloatChannel("Y", ExrPixelType.Float, new[]
                        {
                            1.0f, 2.0f, 3.0f, 4.0f,
                            5.0f, 6.0f, 7.0f, 8.0f,
                            9.0f, 10.0f, 11.0f, 12.0f,
                            13.0f, 14.0f, 15.0f, 16.0f,
                        }),
                    }),
                Channels:
                [
                    ExpectedChannel.Float("Y", new[]
                    {
                        1.0f, 2.0f, 3.0f, 4.0f,
                        5.0f, 6.0f, 7.0f, 8.0f,
                        9.0f, 10.0f, 11.0f, 12.0f,
                        13.0f, 14.0f, 15.0f, 16.0f,
                    }),
                ]),
            new(
                Name: "scan-none-id",
                Compression: CompressionType.None,
                IsTiled: false,
                Width: 2,
                Height: 2,
                ExpectedTileCount: 0,
                Header: new ExrHeader
                {
                    Name = "scan-none-id",
                    Compression = CompressionType.None,
                },
                Image: new ExrImage(
                    2,
                    2,
                    new[]
                    {
                        ExrTestHelper.UIntChannel("ID", ExrPixelType.UInt, new uint[] { 7u, 11u, 13u, 17u }),
                    }),
                Channels:
                [
                    ExpectedChannel.UInt("ID", new uint[] { 7u, 11u, 13u, 17u }),
                ]),
        };

        return new MultipartFixture(
            new ExrMultipartHeader(parts.Select(static part => part.Header)),
            new ExrMultipartImage(parts.Select(static part => part.Image)),
            parts);
    }

    private static void AssertMultipartFixture(byte[] encoded, MultipartFixture fixture)
    {
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromMemory(encoded, out ExrVersion version));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, headers, out ExrMultipartImage images));
        AssertMultipartFixture(fixture, version, headers, images);
    }

    private static void AssertMultipartFixture(
        MultipartFixture fixture,
        ExrVersion version,
        ExrMultipartHeader headers,
        ExrMultipartImage images,
        string? firstPartTypeOverride = null)
    {
        Assert.IsTrue(version.Multipart);
        Assert.IsFalse(version.NonImage);
        Assert.AreEqual(fixture.Parts.Length, headers.Headers.Count);
        Assert.AreEqual(fixture.Parts.Length, images.Images.Count);

        for (int i = 0; i < fixture.Parts.Length; i++)
        {
            ExpectedPart expected = fixture.Parts[i];
            ExrHeader actualHeader = headers.Headers[i];
            ExrImage actualImage = images.Images[i];

            Assert.AreEqual(expected.Name, actualHeader.Name);
            Assert.AreEqual(expected.Compression, actualHeader.Compression);
            Assert.AreEqual(expected.IsTiled, actualHeader.Tiles is not null);
            Assert.AreEqual(expected.Width, actualImage.Width);
            Assert.AreEqual(expected.Height, actualImage.Height);
            Assert.AreEqual(expected.Channels.Length, actualHeader.Channels.Count);
            Assert.AreEqual(expected.Channels.Length, actualImage.Channels.Count);
            Assert.AreEqual(expected.ExpectedTileCount, actualImage.Levels[0].Tiles.Count);

            string expectedPartType = i == 0 && firstPartTypeOverride is not null
                ? firstPartTypeOverride
                : expected.IsTiled ? "tiledimage" : "scanlineimage";
            Assert.AreEqual(expectedPartType, actualHeader.PartType);

            for (int channelIndex = 0; channelIndex < expected.Channels.Length; channelIndex++)
            {
                ExpectedChannel expectedChannel = expected.Channels[channelIndex];
                ExrChannel actualHeaderChannel = actualHeader.Channels[channelIndex];

                Assert.AreEqual(expectedChannel.Name, actualHeaderChannel.Name);
                Assert.AreEqual(expectedChannel.StoredType, actualHeaderChannel.Type);

                if (expectedChannel.FloatSamples is not null)
                {
                    CollectionAssert.AreEqual(expectedChannel.FloatSamples, ExrTestHelper.ReadFloatChannel(actualImage, expectedChannel.Name));
                }
                else
                {
                    CollectionAssert.AreEqual(expectedChannel.UIntSamples!, ExrTestHelper.ReadUIntChannel(actualImage, expectedChannel.Name));
                }
            }
        }
    }

    private readonly record struct MultipartFixture(
        ExrMultipartHeader Headers,
        ExrMultipartImage Images,
        ExpectedPart[] Parts);

    private readonly record struct ExpectedPart(
        string Name,
        CompressionType Compression,
        bool IsTiled,
        int Width,
        int Height,
        int ExpectedTileCount,
        ExrHeader Header,
        ExrImage Image,
        ExpectedChannel[] Channels);

    private readonly record struct ExpectedChannel(
        string Name,
        ExrPixelType StoredType,
        float[]? FloatSamples,
        uint[]? UIntSamples)
    {
        public static ExpectedChannel Float(string name, float[] samples)
        {
            return new ExpectedChannel(name, ExrPixelType.Float, samples, null);
        }

        public static ExpectedChannel UInt(string name, uint[] samples)
        {
            return new ExpectedChannel(name, ExrPixelType.UInt, null, samples);
        }
    }
}
