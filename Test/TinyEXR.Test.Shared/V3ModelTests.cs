using V3 = TinyEXR.V3;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3ModelTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 model enum values match exr.h")]
    public void Case_V3Model_EnumValuesMatchExrHeader()
    {
        AssertEnumValues<V3.ExrResult>(0, 1, -1, -2, -3, -4, -5, -6);
        AssertEnumValues<V3.PixelType>(0, 1, 2);
        AssertEnumValues<V3.Compression>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        AssertEnumValues<V3.LineOrder>(0, 1, 2);
        AssertEnumValues<V3.PartType>(0, 1, 2, 3);
        AssertEnumValues<V3.TileLevelMode>(0, 1, 2);
        AssertEnumValues<V3.TileRoundingMode>(0, 1);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 model geometry and storage ownership are explicit")]
    public void Case_V3Model_GeometryAndStorageOwnershipAreExplicit()
    {
        V3.Box2i wide = new(int.MinValue, -1, int.MaxValue, -1);
        Assert.AreEqual(4294967296L, wide.Width);
        Assert.AreEqual(1L, wide.Height);

        byte[] publicBytes = new byte[] { 1, 2, 3, 4 };
        byte[] attributeBytes = new byte[] { 5, 6, 7 };
        V3.ChannelBuffer publicBuffer = new("R", V3.PixelType.Float, publicBytes);
        V3.HeaderAttribute publicAttribute = new("owner", "string", attributeBytes);

        publicBytes[0] = 99;
        attributeBytes[0] = 99;
        Assert.AreEqual((byte)1, publicBuffer.Data[0]);
        Assert.AreEqual((byte)5, publicAttribute.Data[0]);

        byte[] parserBytes = new byte[] { 8, 9, 10, 11 };
        byte[] parserAttributeBytes = new byte[] { 12 };
        V3.ChannelBuffer adoptedBuffer = V3.ChannelBuffer.Adopt("R", V3.PixelType.Float, parserBytes);
        V3.HeaderAttribute adoptedAttribute = V3.HeaderAttribute.Adopt("source", "string", parserAttributeBytes);
        parserBytes[0] = 42;
        parserAttributeBytes[0] = 43;
        Assert.AreEqual((byte)42, adoptedBuffer.Data[0]);
        Assert.AreEqual((byte)43, adoptedAttribute.Data[0]);

        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 0, 0),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            attributes: new[] { publicAttribute });
        V3.FlatLevel level = FlatLevel(0, 0, header.DataWindow, "R", V3.PixelType.Float, 1);
        V3.Part part = new(header, new[] { level }, isComplete: true);
        V3.Image image = new(new[] { part });

        Assert.IsTrue(part.IsComplete);
        Assert.AreEqual(1L, part.Width);
        Assert.AreEqual(1L, part.Height);
        Assert.IsTrue(((IList<V3.Channel>)header.Channels).IsReadOnly);
        Assert.IsTrue(((IList<V3.PartLevel>)part.Levels).IsReadOnly);
        Assert.IsTrue(((IList<V3.Part>)image.Parts).IsReadOnly);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 model uses strict UTF-8 byte order and tile bounds")]
    public void Case_V3Model_UsesStrictUtf8ByteOrderAndTileBounds()
    {
        const string bmpPrivateUse = "\ue000";
        const string supplementary = "\U00010000";
        V3.Channel[] reverseUtf16Order =
        {
            new V3.Channel(supplementary, V3.PixelType.Half),
            new V3.Channel(bmpPrivateUse, V3.PixelType.Half),
        };

        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 0, 0),
            reverseUtf16Order);
        CollectionAssert.AreEqual(
            new[] { bmpPrivateUse, supplementary },
            header.Channels.Select(channel => channel.Name).ToArray());

        V3.FlatLevel flat = new(
            0,
            0,
            header.DataWindow,
            new[]
            {
                Buffer(supplementary, V3.PixelType.Half, 1),
                Buffer(bmpPrivateUse, V3.PixelType.Half, 1),
            });
        CollectionAssert.AreEqual(
            new[] { bmpPrivateUse, supplementary },
            flat.Channels.Select(channel => channel.Name).ToArray());

        V3.DeepLevel deep = new(
            0,
            0,
            header.DataWindow,
            new[] { 1 },
            reverseUtf16Order,
            new[]
            {
                Buffer(supplementary, V3.PixelType.Half, 1),
                Buffer(bmpPrivateUse, V3.PixelType.Half, 1),
            });
        CollectionAssert.AreEqual(
            new[] { bmpPrivateUse, supplementary },
            deep.Channels.Select(channel => channel.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { bmpPrivateUse, supplementary },
            deep.ChannelDescriptions.Select(channel => channel.Name).ToArray());

        AssertThrows<ArgumentException>(() => new V3.Channel("\ud800", V3.PixelType.Half));
        AssertThrows<ArgumentOutOfRangeException>(() => new V3.TileDescription((uint)int.MaxValue + 1U, 1));
        AssertThrows<ArgumentOutOfRangeException>(() => new V3.TileDescription(1, (uint)int.MaxValue + 1U));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 flat sampling uses absolute inclusive coordinates")]
    public void Case_V3Model_FlatSamplingUsesAbsoluteInclusiveCoordinates()
    {
        V3.Box2i dataWindow = new(-3, -4, 2, 3);
        V3.Header header = new(
            V3.PartType.Scanline,
            dataWindow,
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("S", V3.PixelType.Float, xSampling: 2, ySampling: 3),
            });
        V3.FlatLevel fullLevel = new(
            0,
            0,
            dataWindow,
            new[]
            {
                Buffer("A", V3.PixelType.Half, 48),
                Buffer("S", V3.PixelType.Float, 9),
            });

        V3.Part fullPart = new(header, new[] { fullLevel }, isComplete: true);
        Assert.IsTrue(fullPart.IsComplete);
        Assert.AreEqual(36, fullLevel.GetChannel("S").ByteLength);

        V3.Box2i partialRegion = new(-1, -2, 2, 1);
        V3.FlatLevel partialLevel = new(
            0,
            0,
            partialRegion,
            new[]
            {
                Buffer("A", V3.PixelType.Half, 16),
                Buffer("S", V3.PixelType.Float, 2),
            });
        V3.Part partialPart = new(header, new[] { partialLevel });
        Assert.IsFalse(partialPart.IsComplete);
        Assert.AreEqual(-1, partialPart.Levels[0].Region.MinX);
        Assert.AreEqual(-2, partialPart.Levels[0].Region.MinY);

        V3.FlatLevel wronglyDense = new(
            0,
            0,
            dataWindow,
            new[]
            {
                Buffer("A", V3.PixelType.Half, 48),
                Buffer("S", V3.PixelType.Float, 48),
            });
        AssertThrows<ArgumentException>(() => new V3.Part(header, new[] { wronglyDense }));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 flat sampling permits zero-sample materialized regions")]
    public void Case_V3Model_FlatSamplingPermitsZeroSampleRegions()
    {
        V3.Box2i region = new(1, 1, 1, 1);
        V3.Header header = new(
            V3.PartType.Scanline,
            region,
            new[] { new V3.Channel("S", V3.PixelType.Half, xSampling: 2, ySampling: 3) });
        V3.FlatLevel level = new(
            0,
            0,
            region,
            new[] { Buffer("S", V3.PixelType.Half, 0) });

        V3.Part part = new(header, new[] { level }, isComplete: true);
        Assert.IsTrue(part.IsComplete);
        Assert.AreEqual(0, level.GetChannel("S").ByteLength);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 partial mip and rip levels retain absolute positions")]
    public void Case_V3Model_PartialMipAndRipLevelsRetainAbsolutePositions()
    {
        V3.Header mipHeader = TiledHeader(
            new V3.Box2i(-5, 7, -1, 9),
            V3.TileLevelMode.MipmapLevels,
            V3.TileRoundingMode.RoundDown);
        V3.FlatLevel mipBase = FlatLevel(0, 0, mipHeader.DataWindow, "R", V3.PixelType.Float, 15);
        V3.Part baseOnlyMip = new(mipHeader, new[] { mipBase });
        Assert.IsFalse(baseOnlyMip.IsComplete);

        V3.Box2i tileRegion = new(-3, 8, -2, 9);
        V3.FlatLevel firstTile = FlatLevel(0, 0, tileRegion, "R", V3.PixelType.Float, 4);
        V3.FlatLevel secondTile = FlatLevel(0, 0, new V3.Box2i(-5, 7, -4, 8), "R", V3.PixelType.Float, 4);
        V3.Part tileResult = new(mipHeader, new V3.PartLevel[] { firstTile, secondTile });
        Assert.AreEqual(2, tileResult.GetLevels(0, 0).Count);
        AssertThrows<InvalidOperationException>(() => tileResult.GetLevel(0, 0));
        Assert.AreEqual(-5, tileResult.Levels[0].Region.MinX);
        Assert.AreEqual(7, tileResult.Levels[0].Region.MinY);

        V3.FlatLevel mipLevel1 = FlatLevel(
            1,
            1,
            new V3.Box2i(-5, 7, -4, 7),
            "R",
            V3.PixelType.Float,
            2);
        V3.FlatLevel mipLevel2 = FlatLevel(
            2,
            2,
            new V3.Box2i(-5, 7, -5, 7),
            "R",
            V3.PixelType.Float,
            1);
        V3.Part completeMip = new(
            mipHeader,
            new V3.PartLevel[] { mipLevel2, mipBase, mipLevel1 },
            isComplete: true);
        Assert.IsTrue(completeMip.IsComplete);
        Assert.AreEqual(-5, completeMip.GetLevel(1, 1).Region.MinX);
        AssertThrows<ArgumentException>(() => new V3.Part(mipHeader, new[] { mipBase }, isComplete: true));

        V3.Header ripHeader = TiledHeader(
            new V3.Box2i(10, -4, 13, -3),
            V3.TileLevelMode.RipmapLevels,
            V3.TileRoundingMode.RoundDown);
        V3.FlatLevel ripBase = FlatLevel(0, 0, ripHeader.DataWindow, "R", V3.PixelType.Float, 8);
        V3.Part baseOnlyRip = new(ripHeader, new[] { ripBase });
        Assert.IsFalse(baseOnlyRip.IsComplete);

        V3.FlatLevel ripTail = FlatLevel(
            2,
            1,
            new V3.Box2i(10, -4, 10, -4),
            "R",
            V3.PixelType.Float,
            1);
        V3.Part partialRip = new(ripHeader, new[] { ripTail });
        Assert.AreEqual(10, partialRip.GetLevel(2, 1).Region.MinX);
        Assert.AreEqual(-4, partialRip.GetLevel(2, 1).Region.MinY);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 rectangular RoundUp mip and rip chains are complete")]
    public void Case_V3Model_RectangularRoundUpMipAndRipChainsAreComplete()
    {
        V3.Box2i dataWindow = new(4, -7, 8, -5);
        int[] xSizes = new[] { 5, 3, 2, 1 };
        int[] ySizes = new[] { 3, 2, 1 };

        V3.Header mipHeader = TiledHeader(
            dataWindow,
            V3.TileLevelMode.MipmapLevels,
            V3.TileRoundingMode.RoundUp);
        List<V3.PartLevel> mipLevels = new();
        for (int level = 0; level < xSizes.Length; level++)
        {
            int width = xSizes[level];
            int height = ySizes[Math.Min(level, ySizes.Length - 1)];
            V3.Box2i region = LevelRegion(dataWindow, width, height);
            mipLevels.Add(FlatLevel(
                level,
                level,
                region,
                "R",
                V3.PixelType.Float,
                checked(width * height)));
        }

        V3.Part mipPart = new(mipHeader, mipLevels.AsEnumerable().Reverse(), isComplete: true);
        Assert.IsTrue(mipPart.IsComplete);
        Assert.AreEqual(4, mipPart.Levels.Count);
        Assert.AreEqual(2L, mipPart.GetLevel(2, 2).Width);
        Assert.AreEqual(1L, mipPart.GetLevel(2, 2).Height);
        Assert.AreEqual(4, mipPart.GetLevel(3, 3).Region.MinX);
        Assert.AreEqual(-7, mipPart.GetLevel(3, 3).Region.MinY);

        V3.Header ripHeader = TiledHeader(
            dataWindow,
            V3.TileLevelMode.RipmapLevels,
            V3.TileRoundingMode.RoundUp);
        List<V3.PartLevel> ripLevels = new();
        for (int levelY = 0; levelY < ySizes.Length; levelY++)
        {
            for (int levelX = 0; levelX < xSizes.Length; levelX++)
            {
                int width = xSizes[levelX];
                int height = ySizes[levelY];
                V3.Box2i region = LevelRegion(dataWindow, width, height);
                ripLevels.Add(FlatLevel(
                    levelX,
                    levelY,
                    region,
                    "R",
                    V3.PixelType.Float,
                    checked(width * height)));
            }
        }

        V3.Part ripPart = new(ripHeader, ripLevels.AsEnumerable().Reverse(), isComplete: true);
        Assert.IsTrue(ripPart.IsComplete);
        Assert.AreEqual(12, ripPart.Levels.Count);
        Assert.AreEqual(2L, ripPart.GetLevel(2, 1).Width);
        Assert.AreEqual(2L, ripPart.GetLevel(2, 1).Height);
        Assert.AreEqual(1L, ripPart.GetLevel(3, 2).Width);
        Assert.AreEqual(1L, ripPart.GetLevel(3, 2).Height);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 part rejects channel set and geometry mismatches")]
    public void Case_V3Model_PartRejectsChannelSetAndGeometryMismatches()
    {
        V3.Header header = new(
            V3.PartType.Scanline,
            new V3.Box2i(-1, 2, -1, 2),
            new[]
            {
                new V3.Channel("A", V3.PixelType.Half),
                new V3.Channel("R", V3.PixelType.Float),
            });

        AssertThrows<ArgumentException>(() => new V3.Part(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    header.DataWindow,
                    new[] { Buffer("R", V3.PixelType.Float, 1) }),
            }));
        AssertThrows<ArgumentException>(() => new V3.Part(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    header.DataWindow,
                    new[]
                    {
                        Buffer("A", V3.PixelType.Half, 1),
                        Buffer("R", V3.PixelType.Float, 1),
                        Buffer("Z", V3.PixelType.Float, 1),
                    }),
            }));
        AssertThrows<ArgumentException>(() => new V3.Part(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    header.DataWindow,
                    new[]
                    {
                        Buffer("A", V3.PixelType.Float, 1),
                        Buffer("R", V3.PixelType.Float, 1),
                    }),
            }));
        AssertThrows<ArgumentException>(() => new V3.Part(
            header,
            new[]
            {
                new V3.FlatLevel(
                    0,
                    0,
                    new V3.Box2i(-2, 2, -1, 2),
                    new[]
                    {
                        Buffer("A", V3.PixelType.Half, 2),
                        Buffer("R", V3.PixelType.Float, 2),
                    }),
            }));

        V3.FlatLevel valid = new(
            0,
            0,
            header.DataWindow,
            new[]
            {
                Buffer("A", V3.PixelType.Half, 1),
                Buffer("R", V3.PixelType.Float, 1),
            });
        AssertThrows<ArgumentException>(() => new V3.Part(header, new V3.PartLevel[] { valid, valid }));

        V3.Header deepHeader = new(
            V3.PartType.DeepScanline,
            header.DataWindow,
            new[] { new V3.Channel("Z", V3.PixelType.Float) });
        AssertThrows<ArgumentException>(() => new V3.Part(deepHeader, new[] { valid }));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep storage validates counts ownership and limits")]
    public void Case_V3Model_DeepStorageValidatesCountsOwnershipAndLimits()
    {
        V3.Box2i region = new(-1, 3, 0, 4);
        int[] counts = new[] { 2, 0, 1, 3 };
        byte[] samples = Enumerable.Range(0, 24).Select(static value => (byte)value).ToArray();
        V3.DeepLevel level = new(
            0,
            0,
            region,
            counts,
            new[] { new V3.ChannelBuffer("Z", V3.PixelType.Float, samples) });
        V3.Header header = new(
            V3.PartType.DeepScanline,
            region,
            new[] { new V3.Channel("Z", V3.PixelType.Float) });
        V3.Part part = new(header, new[] { level }, isComplete: true);

        counts[0] = 99;
        samples[0] = 99;
        Assert.IsTrue(part.IsComplete);
        Assert.AreEqual(2, level.SampleCounts[0]);
        Assert.AreEqual((byte)0, level.GetChannel("Z").Data[0]);
        Assert.AreEqual((ulong)6, level.TotalSamples);
        Assert.AreEqual((ulong)6, level.GetChannelSampleCount("Z"));
        Assert.AreEqual((ulong)3, level.GetSampleRange(3).Offset);
        Assert.AreEqual((ulong)3, level.GetSampleRange("Z", 3).Offset);
        Assert.AreEqual(12, level.GetSamples("Z", 3).Length);

        int[] parserCounts = new[] { 1 };
        V3.DeepLevel adopted = V3.DeepLevel.Adopt(
            0,
            0,
            new V3.Box2i(0, 0, 0, 0),
            parserCounts,
            new[] { V3.ChannelBuffer.Adopt("Z", V3.PixelType.Float, new byte[4]) });
        parserCounts[0] = 0;
        Assert.AreEqual(0, adopted.SampleCounts[0]);

        AssertThrows<ArgumentException>(() => new V3.DeepLevel(
            0,
            0,
            region,
            new[] { 1, 2, 3 },
            new[] { Buffer("Z", V3.PixelType.Float, 6) }));
        AssertThrows<ArgumentException>(() => new V3.DeepLevel(
            0,
            0,
            new V3.Box2i(0, 0, 0, 0),
            new[] { -1 },
            new[] { Buffer("Z", V3.PixelType.Float, 0) }));
        AssertThrows<ArgumentException>(() => new V3.DeepLevel(
            0,
            0,
            new V3.Box2i(0, 0, 0, 0),
            new[] { 2 },
            new[] { Buffer("Z", V3.PixelType.Float, 1) }));
        AssertThrows<NotSupportedException>(() => new V3.DeepLevel(
            0,
            0,
            new V3.Box2i(0, 0, 0, 0),
            new[] { int.MaxValue },
            new[] { Buffer("Z", V3.PixelType.Float, 0) }));
        AssertThrows<NotSupportedException>(() => new V3.DeepLevel(
            0,
            0,
            new V3.Box2i(0, 0, 50000, 50000),
            Array.Empty<int>(),
            new[] { Buffer("Z", V3.PixelType.Float, 0) }));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 deep subsampling uses per-channel sampled-pixel prefixes")]
    public void Case_V3Model_DeepSubsamplingUsesPerChannelSampledPixelPrefixes()
    {
        V3.Box2i region = new(-2, -3, 2, 0);
        int[] sampleCounts = Enumerable.Range(1, 20).ToArray();
        V3.Channel denseChannel = new("R", V3.PixelType.Float);
        V3.Channel sampledChannel = new("S", V3.PixelType.Half, xSampling: 2, ySampling: 3);
        V3.Header header = new(
            V3.PartType.DeepScanline,
            region,
            new[] { sampledChannel, denseChannel });
        V3.DeepLevel level = new(
            0,
            0,
            region,
            sampleCounts,
            header.Channels,
            new[]
            {
                Buffer("R", V3.PixelType.Float, 210),
                Buffer("S", V3.PixelType.Half, 63),
            });

        V3.Part part = new(header, new[] { level }, isComplete: true);
        Assert.IsTrue(part.IsComplete);
        Assert.AreEqual((ulong)210, level.TotalSamples);
        Assert.AreEqual((ulong)210, level.GetChannelSampleCount("R"));
        Assert.AreEqual((ulong)63, level.GetChannelSampleCount("S"));

        V3.DeepSampleRange denseRange = level.GetSampleRange("R", 1);
        Assert.AreEqual((ulong)1, denseRange.Offset);
        Assert.AreEqual(2, denseRange.Count);

        V3.DeepSampleRange firstUnsampled = level.GetSampleRange("S", 1);
        Assert.AreEqual((ulong)1, firstUnsampled.Offset);
        Assert.AreEqual(0, firstUnsampled.Count);
        Assert.AreEqual(0, level.GetSamples("S", 1).Length);

        V3.DeepSampleRange sampled = level.GetSampleRange("S", 15);
        Assert.AreEqual((ulong)9, sampled.Offset);
        Assert.AreEqual(16, sampled.Count);
        Assert.AreEqual(32, level.GetSamples("S", 15).Length);

        V3.DeepSampleRange middleUnsampled = level.GetSampleRange("S", 16);
        Assert.AreEqual((ulong)25, middleUnsampled.Offset);
        Assert.AreEqual(0, middleUnsampled.Count);
        V3.DeepSampleRange finalSampled = level.GetSampleRange("S", 19);
        Assert.AreEqual((ulong)43, finalSampled.Offset);
        Assert.AreEqual(20, finalSampled.Count);

        AssertThrows<ArgumentException>(() => new V3.DeepLevel(
            0,
            0,
            region,
            sampleCounts,
            header.Channels,
            new[]
            {
                Buffer("R", V3.PixelType.Float, 210),
                Buffer("S", V3.PixelType.Half, 64),
            }));

        V3.DeepLevel incorrectlyDenseDescription = new(
            0,
            0,
            region,
            sampleCounts,
            new[]
            {
                Buffer("R", V3.PixelType.Float, 210),
                Buffer("S", V3.PixelType.Half, 210),
            });
        AssertThrows<ArgumentException>(() => new V3.Part(header, new[] { incorrectlyDenseDescription }));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 materialized buffer limits are explicit")]
    public void Case_V3Model_MaterializedBufferLimitsAreExplicit()
    {
        V3.Box2i hugeRegion = new(0, 0, int.MaxValue, 0);
        V3.Header header = new(
            V3.PartType.Scanline,
            hugeRegion,
            new[] { new V3.Channel("A", V3.PixelType.Half) });
        V3.FlatLevel level = new(
            0,
            0,
            hugeRegion,
            new[] { Buffer("A", V3.PixelType.Half, 0) });

        Assert.AreEqual(2147483648L, hugeRegion.Width);
        AssertThrows<NotSupportedException>(() => new V3.Part(header, new[] { level }));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 mixed multipart names remain unambiguous")]
    public void Case_V3Model_MixedMultipartNamesRemainUnambiguous()
    {
        V3.Part flatPart = NamedFlatPart("beauty");
        V3.Box2i region = new(0, 0, 0, 0);
        V3.Header deepHeader = new(
            V3.PartType.DeepScanline,
            region,
            new[] { new V3.Channel("Z", V3.PixelType.Half) },
            name: "depth");
        V3.Part deepPart = new(
            deepHeader,
            new[]
            {
                new V3.DeepLevel(
                    0,
                    0,
                    region,
                    new[] { 2 },
                    new[] { Buffer("Z", V3.PixelType.Half, 2) }),
            },
            isComplete: true);
        V3.Image image = new(new[] { flatPart, deepPart });

        Assert.IsTrue(image.IsMultipart);
        Assert.AreSame(flatPart, image.GetPart("beauty"));
        Assert.AreSame(deepPart, image.GetPart("depth"));
        AssertThrows<ArgumentException>(() => new V3.Image(new[] { NamedFlatPart("same"), NamedFlatPart("same") }));
        AssertThrows<ArgumentException>(() => new V3.Image(new[] { NamedFlatPart(string.Empty), NamedFlatPart("named") }));
    }

    private static V3.Header TiledHeader(
        V3.Box2i dataWindow,
        V3.TileLevelMode levelMode,
        V3.TileRoundingMode roundingMode)
    {
        return new V3.Header(
            V3.PartType.Tiled,
            dataWindow,
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            tiles: new V3.TileDescription(2, 2, levelMode, roundingMode));
    }

    private static V3.Box2i LevelRegion(V3.Box2i dataWindow, int width, int height)
    {
        return new V3.Box2i(
            dataWindow.MinX,
            dataWindow.MinY,
            checked(dataWindow.MinX + width - 1),
            checked(dataWindow.MinY + height - 1));
    }

    private static V3.FlatLevel FlatLevel(
        int levelX,
        int levelY,
        V3.Box2i region,
        string channelName,
        V3.PixelType pixelType,
        int sampleCount)
    {
        return new V3.FlatLevel(
            levelX,
            levelY,
            region,
            new[] { Buffer(channelName, pixelType, sampleCount) });
    }

    private static V3.Part NamedFlatPart(string name)
    {
        V3.Box2i region = new(0, 0, 0, 0);
        V3.Header header = new(
            V3.PartType.Scanline,
            region,
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            name: name);
        return new V3.Part(
            header,
            new[] { FlatLevel(0, 0, region, "R", V3.PixelType.Float, 1) },
            isComplete: true);
    }

    private static V3.ChannelBuffer Buffer(string name, V3.PixelType pixelType, int sampleCount)
    {
        int sampleSize = pixelType == V3.PixelType.Half ? 2 : 4;
        return new V3.ChannelBuffer(name, pixelType, new byte[checked(sampleCount * sampleSize)]);
    }

    private static void AssertEnumValues<T>(params int[] expected)
        where T : struct
    {
        Array values = Enum.GetValues(typeof(T));
        int[] actual = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            actual[i] = Convert.ToInt32(values.GetValue(i));
        }

        CollectionAssert.AreEquivalent(expected, actual, typeof(T).FullName);
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but caught {exception.GetType().Name}: {exception.Message}");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
