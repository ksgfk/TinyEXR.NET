using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using V3 = TinyEXR.V3;
using V3Codecs = TinyEXR.V3.Codecs;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Test;

[TestClass]
public sealed class V1FacadeV3CoreTests
{
    private static readonly V3.Box2i DataWindow = new V3.Box2i(-1, 2, 1, 3);

    private static readonly ushort[] HalfBits =
    {
        0x3c00,
        0xc000,
        0x3800,
        0x0000,
        0x7bff,
        0x0400,
    };

    private static readonly float[] HalfValues =
    {
        1.0f,
        -2.0f,
        0.5f,
        0.0f,
        65504.0f,
        0.00006103515625f,
    };

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 memory facade delegates ZSTD scanline reads to V3")]
    public void Case_V1MemoryFacade_DelegatesZstdScanlineReadsToV3()
    {
        byte[] customValue = Encoding.UTF8.GetBytes("v3-core\0");
        V3.Header sourceHeader = new V3.Header(
            V3.PartType.Scanline,
            DataWindow,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt),
            },
            compression: V3.Compression.ZSTD,
            attributes: new[]
            {
                new V3.HeaderAttribute("facadeSource", "string", customValue),
            });
        byte[] encoded = WriteImage(sourceHeader);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out ExrVersion version, out ExrHeader header));
        Assert.AreEqual(2, version.Version);
        Assert.IsFalse(version.Tiled);
        Assert.IsFalse(version.NonImage);
        Assert.IsFalse(version.Multipart);
        Assert.AreEqual(CompressionType.ZSTD, header.Compression);
        Assert.AreEqual(DataWindow.MinX, header.DataWindow.MinX);
        Assert.AreEqual(DataWindow.MinY, header.DataWindow.MinY);
        Assert.AreEqual(DataWindow.MaxX, header.DataWindow.MaxX);
        Assert.AreEqual(DataWindow.MaxY, header.DataWindow.MaxY);
        CollectionAssert.AreEqual(
            new[] { "F", "H", "U" },
            header.Channels.Select(static channel => channel.Name).ToArray());
        Assert.AreEqual(1, header.CustomAttributes.Count);
        Assert.AreEqual("facadeSource", header.CustomAttributes[0].Name);
        Assert.AreEqual("string", header.CustomAttributes[0].TypeName);
        CollectionAssert.AreEqual(customValue, header.CustomAttributes[0].Value);

        ExrChannel halfChannel = header.Channels.Single(static channel => channel.Name == "H");
        halfChannel.RequestedPixelType = ExrPixelType.Float;
        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadImage(encoded, header, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromMemory(encoded, header, out ExrImage image));
        Assert.AreEqual(DataWindow.Width, image.Width);
        Assert.AreEqual(DataWindow.Height, image.Height);

        ExrImageChannel floatChannel = image.GetChannel("F");
        Assert.AreEqual(ExrPixelType.Float, floatChannel.DataType);
        CollectionAssert.AreEqual(CreateFloatBytes(), floatChannel.Data);

        ExrImageChannel convertedHalfChannel = image.GetChannel("H");
        Assert.AreEqual(ExrPixelType.Float, convertedHalfChannel.DataType);
        CollectionAssert.AreEqual(
            new[] { 1.0f, -2.0f, 0.5f, 0.0f, 65504.0f, 0.00006103515625f },
            ReadFloats(convertedHalfChannel.Data));

        ExrImageChannel uintChannel = image.GetChannel("U");
        Assert.AreEqual(ExrPixelType.UInt, uintChannel.DataType);
        CollectionAssert.AreEqual(CreateUIntValues(), ReadUInts(uintChannel.Data));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 stream facade delegates ZSTD reads and preserves its origin")]
    public void Case_V1StreamFacade_DelegatesZstdReadsAndPreservesOrigin()
    {
        V3.Header sourceHeader = new V3.Header(
            V3.PartType.Scanline,
            DataWindow,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt),
            },
            compression: V3.Compression.ZSTD,
            attributes: new[]
            {
                new V3.HeaderAttribute("streamSource", "string", Encoding.UTF8.GetBytes("v3-stream\0")),
            });
        byte[] encoded = WriteImage(sourceHeader);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out ExrVersion expectedVersion, out ExrHeader expectedHeader));

        const int origin = 17;
        byte[] container = new byte[checked(origin + encoded.Length)];
        encoded.CopyTo(container, origin);
        using MemoryStream stream = new MemoryStream(container, writable: false);
        stream.Position = origin;

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromStream(stream, out ExrVersion streamVersion, out ExrHeader streamHeader));
        Assert.AreEqual(origin, stream.Position);
        Assert.AreEqual(expectedVersion.Version, streamVersion.Version);
        Assert.AreEqual(expectedVersion.Tiled, streamVersion.Tiled);
        Assert.AreEqual(expectedVersion.LongName, streamVersion.LongName);
        Assert.AreEqual(expectedVersion.NonImage, streamVersion.NonImage);
        Assert.AreEqual(expectedVersion.Multipart, streamVersion.Multipart);
        ExrTestHelper.EqualHeaders(expectedHeader, streamHeader);

        streamHeader.Channels.Single(static channel => channel.Name == "H").RequestedPixelType = ExrPixelType.Float;
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromStream(stream, streamHeader, out ExrImage image));
        Assert.AreEqual(origin, stream.Position);
        CollectionAssert.AreEqual(CreateFloatBytes(), image.GetChannel("F").Data);
        CollectionAssert.AreEqual(
            new[] { 1.0f, -2.0f, 0.5f, 0.0f, 65504.0f, 0.00006103515625f },
            ReadFloats(image.GetChannel("H").Data));
        CollectionAssert.AreEqual(CreateUIntValues(), ReadUInts(image.GetChannel("U").Data));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 headerless image entrypoints delegate ZSTD reads to V3")]
    public void Case_V1HeaderlessImageEntrypoints_DelegateZstdReadsToV3()
    {
        byte[] customValue = Encoding.UTF8.GetBytes("headerless-v3\0");
        V3.Header sourceHeader = new V3.Header(
            V3.PartType.Scanline,
            DataWindow,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt),
            },
            compression: V3.Compression.ZSTD,
            attributes: new[]
            {
                new V3.HeaderAttribute("headerlessSource", "string", customValue),
            });
        byte[] encoded = WriteImage(sourceHeader);

        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadImage(encoded, out _, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.TryReadImage(encoded, out ExrHeader memoryHeader, out ExrImage memoryImage));
        Assert.AreEqual(CompressionType.ZSTD, memoryHeader.Compression);
        CollectionAssert.AreEqual(customValue, memoryHeader.CustomAttributes.Single().Value);
        CollectionAssert.AreEqual(CreateFloatBytes(), memoryImage.GetChannel("F").Data);
        CollectionAssert.AreEqual(HalfBits, MemoryMarshal.Cast<byte, ushort>(
            memoryImage.GetChannel("H").Data).ToArray());
        CollectionAssert.AreEqual(CreateUIntValues(), ReadUInts(memoryImage.GetChannel("U").Data));

        const int origin = 19;
        byte[] container = new byte[checked(origin + encoded.Length)];
        encoded.CopyTo(container, origin);
        using MemoryStream stream = new MemoryStream(container, writable: false);
        stream.Position = origin;
        Assert.AreEqual(
            ResultCode.Success,
            Exr.TryReadImage(stream, out ExrHeader streamHeader, out ExrImage streamImage));
        Assert.AreEqual(origin, stream.Position);
        ExrTestHelper.EqualHeaders(memoryHeader, streamHeader);
        ExrTestHelper.EqualImages(memoryImage, streamImage);

        string path = Path.Combine(Path.GetTempPath(), $"TinyEXRNet-Headerless-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);
            Assert.AreEqual(
                ResultCode.Success,
                Exr.TryReadImage(path, out ExrHeader fileHeader, out ExrImage fileImage));
            ExrTestHelper.EqualHeaders(memoryHeader, fileHeader);
            ExrTestHelper.EqualImages(memoryImage, fileImage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 save facade emits genuine ZSTD frames through V3")]
    public void Case_V1SaveFacade_EmitsGenuineZstdFramesThroughV3()
    {
        const int width = 1024;
        const int height = 32;
        byte[] expected = CreateRepeatingFloatBytes(width * height);
        ExrImage image = new ExrImage(
            width,
            height,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("R", ExrPixelType.Float),
                    ExrPixelType.Float,
                    expected),
            });
        ExrHeader header = new ExrHeader
        {
            Compression = CompressionType.ZSTD,
        };

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            Assert.AreEqual(V3.Compression.ZSTD, reader.GetHeader(0).Compression);
            Assert.AreEqual(1, reader.GetNumBlocks(0));
            V3.BlockInfo block = reader.GetBlockInfo(0, 0);
            int rawSize = checked((int)block.UncompressedByteCount!.Value);
            int chunkOffset = checked((int)block.FileOffset);
            int packedSize = BinaryPrimitives.ReadInt32LittleEndian(
                encoded.AsSpan(chunkOffset + sizeof(int), sizeof(int)));
            Assert.IsTrue(packedSize < rawSize);

            ReadOnlySpan<byte> payload = encoded.AsSpan(
                chunkOffset + block.ChunkHeaderByteCount,
                packedSize);
            byte[] decodedPayload = new byte[rawSize];
            Assert.AreEqual(
                V3Codecs.ZstdFrameStatus.Success,
                V3Codecs.ZstdFrameDecoder.Decode(
                    payload,
                    decodedPayload,
                    out int consumed,
                    out int written,
                    out _));
            Assert.AreEqual(payload.Length, consumed);
            Assert.AreEqual(decodedPayload.Length, written);
            CollectionAssert.AreEqual(expected, decodedPayload);
        }

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
        Assert.AreEqual(CompressionType.ZSTD, decodedHeader.Compression);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));
        CollectionAssert.AreEqual(expected, decodedImage.GetChannel("R").Data);
        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadRgba(encoded, layerName: null, out _, out _, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRFromMemory(encoded, out float[] rgba, out int rgbaWidth, out int rgbaHeight));
        Assert.AreEqual(width, rgbaWidth);
        Assert.AreEqual(height, rgbaHeight);
        float[] expectedFloats = ReadFloats(expected);
        for (int pixelIndex = 0; pixelIndex < expectedFloats.Length; pixelIndex++)
        {
            int rgbaOffset = pixelIndex * 4;
            Assert.AreEqual(expectedFloats[pixelIndex], rgba[rgbaOffset + 0]);
            Assert.AreEqual(expectedFloats[pixelIndex], rgba[rgbaOffset + 1]);
            Assert.AreEqual(expectedFloats[pixelIndex], rgba[rgbaOffset + 2]);
            Assert.AreEqual(expectedFloats[pixelIndex], rgba[rgbaOffset + 3]);
        }

        string path = Path.Combine(Path.GetTempPath(), $"TinyEXRNet-V1V3-{Guid.NewGuid():N}.exr");
        try
        {
            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToFile(image, header, path));
            Assert.AreEqual(
                ResultCode.Success,
                Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader fileHeader));
            Assert.AreEqual(CompressionType.ZSTD, fileHeader.Compression);
            Assert.AreEqual(
                ResultCode.Success,
                Exr.LoadEXRImageFromFile(path, fileHeader, out ExrImage fileImage));
            CollectionAssert.AreEqual(expected, fileImage.GetChannel("R").Data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 facade emits and reads genuine HTJ2K through V3")]
    public void Case_V1Facade_EmitsAndReadsGenuineHtj2kThroughV3()
    {
        const int width = 128;
        const int height = 32;
        byte[] expected = CreateRepeatingFloatBytes(width * height);
        ExrImage image = new ExrImage(
            width,
            height,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("R", ExrPixelType.Float),
                    ExrPixelType.Float,
                    expected),
            });

        foreach (CompressionType compression in new[]
        {
            CompressionType.HTJ2K32,
            CompressionType.HTJ2K256,
        })
        {
            ExrHeader header = new ExrHeader { Compression = compression };
            Assert.AreEqual(
                ResultCode.Success,
                Exr.SaveEXRImageToMemory(image, header, out byte[] encoded),
                compression.ToString());

            using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
            {
                Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
                Assert.AreEqual((V3.Compression)(int)compression, reader.GetHeader(0).Compression);
                Assert.AreEqual(1, reader.GetNumBlocks(0));
                V3.BlockInfo block = reader.GetBlockInfo(0, 0);
                int chunkOffset = checked((int)block.FileOffset);
                int packedSize = BinaryPrimitives.ReadInt32LittleEndian(
                    encoded.AsSpan(chunkOffset + sizeof(int), sizeof(int)));
                Assert.IsTrue(packedSize < expected.Length, compression.ToString());
                ReadOnlySpan<byte> payload = encoded.AsSpan(
                    chunkOffset + block.ChunkHeaderByteCount,
                    packedSize);
                Assert.IsTrue(payload.Length >= 2);
                Assert.AreEqual((byte)'H', payload[0]);
                Assert.AreEqual((byte)'T', payload[1]);
            }

            Assert.AreEqual(
                ResultCode.Success,
                Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
            Assert.AreEqual(compression, decodedHeader.Compression);
            Assert.AreEqual(
                ResultCode.UnsupportedFeature,
                PortV1.ExrImplementation.TryReadImage(encoded, decodedHeader, out _));
            Assert.AreEqual(
                ResultCode.Success,
                Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));
            CollectionAssert.AreEqual(expected, decodedImage.GetChannel("R").Data, compression.ToString());
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 layers and RGBA facade delegate ZSTD reads to V3")]
    public void Case_V1LayersAndRgbaFacade_DelegateZstdReadsToV3()
    {
        const int width = 128;
        const int height = 32;
        ExrImage image = new ExrImage(
            width,
            height,
            new[]
            {
                FloatImageChannel("beauty.B", width, height, 0.25f),
                FloatImageChannel("beauty.G", width, height, 0.5f),
                FloatImageChannel("beauty.R", width, height, 0.75f),
                FloatImageChannel("depth.Z", width, height, 2.0f),
            });
        ExrHeader header = new ExrHeader
        {
            Compression = CompressionType.ZSTD,
        };
        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadRgba(encoded, "beauty", out _, out _, out _));

        const int origin = 11;
        byte[] container = new byte[checked(origin + encoded.Length)];
        encoded.CopyTo(container, origin);
        using MemoryStream stream = new MemoryStream(container, writable: false);
        stream.Position = origin;

        Assert.AreEqual(ResultCode.Success, Exr.EXRLayersFromStream(stream, out string[] layers));
        Assert.AreEqual(origin, stream.Position);
        CollectionAssert.AreEqual(new[] { "beauty", "depth" }, layers);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRWithLayerFromStream(stream, "beauty", out float[] beauty, out int beautyWidth, out int beautyHeight));
        Assert.AreEqual(origin, stream.Position);
        Assert.AreEqual(width, beautyWidth);
        Assert.AreEqual(height, beautyHeight);
        AssertRgba(beauty, 0.75f, 0.5f, 0.25f, 1.0f);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRWithLayerFromStream(stream, "depth", out float[] depth, out _, out _));
        Assert.AreEqual(origin, stream.Position);
        AssertRgba(depth, 2.0f, 2.0f, 2.0f, 2.0f);

        Assert.AreEqual(
            ResultCode.LayerNotFound,
            Exr.LoadEXRWithLayerFromStream(stream, "missing", out _, out _, out _));
        Assert.AreEqual(origin, stream.Position);
        Assert.AreEqual(
            ResultCode.LayerNotFound,
            Exr.LoadEXRFromStream(stream, out _, out _, out _));
        Assert.AreEqual(origin, stream.Position);

        string path = Path.Combine(Path.GetTempPath(), $"TinyEXRNet-V1Layers-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);
            Assert.AreEqual(ResultCode.Success, Exr.EXRLayers(path, out string[] fileLayers));
            CollectionAssert.AreEqual(layers, fileLayers);
            Assert.AreEqual(
                ResultCode.Success,
                Exr.LoadEXRWithLayer(path, "beauty", out float[] fileBeauty, out _, out _));
            CollectionAssert.AreEqual(beauty, fileBeauty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 tiled facade materializes ZSTD levels and tiles through V3")]
    public void Case_V1TiledFacade_MaterializesZstdLevelsAndTilesThroughV3()
    {
        V3.Box2i dataWindow = new V3.Box2i(-2, 3, 2, 6);
        V3.Header sourceHeader = new V3.Header(
            V3.PartType.Tiled,
            dataWindow,
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD,
            tiles: new V3.TileDescription(3, 2));
        byte[] expected = CreateRepeatingFloatBytes(
            checked((int)(dataWindow.Width * dataWindow.Height)));
        byte[] encoded = WriteTiledImage(sourceHeader, expected);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader header));
        Assert.AreEqual(CompressionType.ZSTD, header.Compression);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadImage(encoded, header, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromMemory(encoded, header, out ExrImage image));

        Assert.AreEqual(1, image.Levels.Count);
        ExrImageLevel level = image.Levels[0];
        Assert.AreEqual(5, level.Width);
        Assert.AreEqual(4, level.Height);
        CollectionAssert.AreEqual(expected, level.Channels.Single().Data);
        Assert.AreEqual(4, level.Tiles.Count);
        AssertTile(level.Tiles[0], 0, 0, 3, 2, expected, level.Width);
        AssertTile(level.Tiles[1], 3, 0, 2, 2, expected, level.Width);
        AssertTile(level.Tiles[2], 0, 2, 3, 2, expected, level.Width);
        AssertTile(level.Tiles[3], 3, 2, 2, 2, expected, level.Width);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRFromMemory(encoded, out float[] rgba, out int width, out int height));
        Assert.AreEqual(level.Width, width);
        Assert.AreEqual(level.Height, height);
        float[] values = ReadFloats(expected);
        for (int pixelIndex = 0; pixelIndex < values.Length; pixelIndex++)
        {
            int offset = pixelIndex * 4;
            Assert.AreEqual(values[pixelIndex], rgba[offset + 0]);
            Assert.AreEqual(values[pixelIndex], rgba[offset + 1]);
            Assert.AreEqual(values[pixelIndex], rgba[offset + 2]);
            Assert.AreEqual(values[pixelIndex], rgba[offset + 3]);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 multipart facade delegates heterogeneous ZSTD parts to V3")]
    public void Case_V1MultipartFacade_DelegatesHeterogeneousZstdPartsToV3()
    {
        V3.Header scanHeader = new V3.Header(
            V3.PartType.Scanline,
            new V3.Box2i(0, 0, 127, 31),
            new[] { new V3.Channel("R", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD,
            name: "scan");
        V3.Header tileHeader = new V3.Header(
            V3.PartType.Tiled,
            new V3.Box2i(-2, 3, 2, 6),
            new[] { new V3.Channel("Y", V3.PixelType.Float) },
            compression: V3.Compression.ZSTD,
            tiles: new V3.TileDescription(3, 2),
            name: "tile");
        byte[] scanData = CreateRepeatingFloatBytes(
            checked((int)(scanHeader.DataWindow.Width * scanHeader.DataWindow.Height)));
        byte[] tileData = CreateRepeatingFloatBytes(
            checked((int)(tileHeader.DataWindow.Width * tileHeader.DataWindow.Height)));
        byte[] encoded = WriteMultipartImage(
            new[] { scanHeader, tileHeader },
            new[] { scanData, tileData });

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRMultipartHeaderFromMemory(encoded, out ExrVersion version, out ExrMultipartHeader headers));
        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(2, headers.Headers.Count);
        Assert.AreEqual("scan", headers.Headers[0].Name);
        Assert.AreEqual("scanlineimage", headers.Headers[0].PartType);
        Assert.AreEqual("tile", headers.Headers[1].Name);
        Assert.AreEqual("tiledimage", headers.Headers[1].PartType);
        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadMultipartImages(
                encoded,
                headers.Headers.ToArray(),
                out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRMultipartImageFromMemory(encoded, headers, out ExrMultipartImage images));
        Assert.AreEqual(2, images.Images.Count);
        CollectionAssert.AreEqual(scanData, images.Images[0].GetChannel("R").Data);
        CollectionAssert.AreEqual(tileData, images.Images[1].GetChannel("Y").Data);
        Assert.AreEqual(4, images.Images[1].Levels[0].Tiles.Count);

        const int origin = 23;
        byte[] container = new byte[checked(origin + encoded.Length)];
        encoded.CopyTo(container, origin);
        using MemoryStream stream = new MemoryStream(container, writable: false);
        stream.Position = origin;
        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRMultipartHeaderFromStream(stream, out _, out ExrMultipartHeader streamHeaders));
        Assert.AreEqual(origin, stream.Position);
        Assert.AreEqual(headers.Headers.Count, streamHeaders.Headers.Count);
        for (int partIndex = 0; partIndex < headers.Headers.Count; partIndex++)
        {
            ExrTestHelper.EqualHeaders(headers.Headers[partIndex], streamHeaders.Headers[partIndex]);
        }

        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRMultipartImageFromStream(stream, streamHeaders, out ExrMultipartImage streamImages));
        Assert.AreEqual(origin, stream.Position);
        ExrTestHelper.EqualImages(images.Images[0], streamImages.Images[0]);
        ExrTestHelper.EqualImages(images.Images[1], streamImages.Images[1]);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 multipart facade round-trips one part through V3")]
    public void Case_V1MultipartFacade_RoundTripsSinglePartThroughV3()
    {
        const int width = 128;
        const int height = 32;
        byte[] sourceData = CreateRepeatingFloatBytes(width * height);
        ExrImage sourceImage = new(
            width,
            height,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("R", ExrPixelType.Float),
                    ExrPixelType.Float,
                    sourceData),
            });
        ExrHeader sourceHeader = new()
        {
            Name = "only",
            Compression = CompressionType.ZSTD,
        };
        ExrMultipartImage sourceImages = new(new[] { sourceImage });
        ExrMultipartHeader sourceHeaders = new(new[] { sourceHeader });

        Assert.AreEqual(
            ResultCode.Success,
            Exr.SaveEXRMultipartImageToMemory(sourceImages, sourceHeaders, out byte[] encoded));
        Assert.AreNotEqual(
            0U,
            BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)) & (1U << 12));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRMultipartHeaderFromMemory(encoded, out ExrVersion version, out ExrMultipartHeader headers));
        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(1, headers.Headers.Count);
        Assert.AreEqual("only", headers.Headers[0].Name);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRMultipartImageFromMemory(encoded, headers, out ExrMultipartImage images));
        Assert.AreEqual(1, images.Images.Count);
        CollectionAssert.AreEqual(sourceData, images.Images[0].GetChannel("R").Data);

        using MemoryStream stream = new(encoded, writable: false);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRMultipartHeaderFromStream(stream, out _, out ExrMultipartHeader streamHeaders));
        Assert.AreEqual(0, stream.Position);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRMultipartImageFromStream(stream, streamHeaders, out ExrMultipartImage streamImages));
        Assert.AreEqual(0, stream.Position);
        CollectionAssert.AreEqual(sourceData, streamImages.Images[0].GetChannel("R").Data);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 tiled save facade emits ZSTD rip levels through V3")]
    public void Case_V1TiledSaveFacade_EmitsZstdRipLevelsThroughV3()
    {
        const int width = 128;
        const int height = 64;
        ExrImage sourceImage = CreateV1TiledImage(
            width,
            height,
            ExrTileLevelMode.RipMapLevels,
            ExrTileRoundingMode.RoundDown);
        ExrHeader sourceHeader = new ExrHeader
        {
            Compression = CompressionType.ZSTD,
            LineOrder = LineOrderType.RandomY,
            DataWindow = new ExrBox2i(-2, 3, width - 3, height + 2),
            DisplayWindow = new ExrBox2i(-4, 1, width - 1, height + 4),
            Tiles = new ExrTileDescription
            {
                TileSizeX = 32,
                TileSizeY = 16,
                LevelMode = ExrTileLevelMode.RipMapLevels,
                RoundingMode = ExrTileRoundingMode.RoundDown,
            },
        };
        ExrAttribute chromaticities = CreateChromaticitiesAttribute();
        sourceHeader.CustomAttributes.Add(chromaticities);

        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryWriteImage(sourceImage, sourceHeader, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.SaveEXRImageToMemory(sourceImage, sourceHeader, out byte[] encoded));

        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            Assert.AreEqual(V3.PartType.Tiled, reader.GetHeader(0).PartType);
            Assert.AreEqual(V3.Compression.ZSTD, reader.GetHeader(0).Compression);
            AssertPartHasCompressedZstdBlock(encoded, reader, 0);
        }

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out ExrVersion version, out ExrHeader decodedHeader));
        Assert.IsTrue(version.Tiled);
        Assert.AreEqual(sourceHeader.Compression, decodedHeader.Compression);
        Assert.AreEqual(sourceHeader.LineOrder, decodedHeader.LineOrder);
        Assert.AreEqual(sourceHeader.DataWindow.MinX, decodedHeader.DataWindow.MinX);
        Assert.AreEqual(sourceHeader.DataWindow.MinY, decodedHeader.DataWindow.MinY);
        Assert.AreEqual(sourceHeader.DataWindow.MaxX, decodedHeader.DataWindow.MaxX);
        Assert.AreEqual(sourceHeader.DataWindow.MaxY, decodedHeader.DataWindow.MaxY);
        Assert.IsNotNull(decodedHeader.Tiles);
        Assert.AreEqual(ExrTileLevelMode.RipMapLevels, decodedHeader.Tiles!.LevelMode);
        ExrAttribute decodedChromaticities = decodedHeader.CustomAttributes.Single(
            static attribute => attribute.Name == "chromaticities");
        CollectionAssert.AreEqual(chromaticities.Value, decodedChromaticities.Value);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));
        Assert.AreEqual(sourceImage.Levels.Count, decodedImage.Levels.Count);
        for (int levelIndex = 0; levelIndex < sourceImage.Levels.Count; levelIndex++)
        {
            ExrImageLevel expected = sourceImage.Levels[levelIndex];
            ExrImageLevel actual = decodedImage.Levels[levelIndex];
            Assert.AreEqual(expected.LevelX, actual.LevelX);
            Assert.AreEqual(expected.LevelY, actual.LevelY);
            Assert.AreEqual(expected.Width, actual.Width);
            Assert.AreEqual(expected.Height, actual.Height);
            CollectionAssert.AreEqual(expected.Channels[0].Data, actual.Channels[0].Data);
            Assert.IsTrue(actual.Tiles.Count > 0);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 multipart save facade emits heterogeneous ZSTD parts through V3")]
    public void Case_V1MultipartSaveFacade_EmitsHeterogeneousZstdPartsThroughV3()
    {
        const int scanWidth = 1024;
        const int scanHeight = 32;
        byte[] scanData = CreateRepeatingFloatBytes(scanWidth * scanHeight);
        ExrImage scanImage = new ExrImage(
            scanWidth,
            scanHeight,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("R", ExrPixelType.Float),
                    ExrPixelType.Float,
                    scanData),
            });
        ExrImage tileImage = CreateV1TiledImage(
            128,
            64,
            ExrTileLevelMode.OneLevel,
            ExrTileRoundingMode.RoundDown);
        ExrHeader scanHeader = new ExrHeader
        {
            Name = "scan-zstd",
            Compression = CompressionType.ZSTD,
        };
        ExrHeader tileHeader = new ExrHeader
        {
            Name = "tile-zstd",
            Compression = CompressionType.ZSTD,
            LineOrder = LineOrderType.RandomY,
            Tiles = new ExrTileDescription
            {
                TileSizeX = 32,
                TileSizeY = 16,
                LevelMode = ExrTileLevelMode.OneLevel,
                RoundingMode = ExrTileRoundingMode.RoundDown,
            },
        };
        ExrImage[] sourceImages = { scanImage, tileImage };
        ExrHeader[] sourceHeaders = { scanHeader, tileHeader };
        ExrMultipartImage multipartImage = new ExrMultipartImage(sourceImages);
        ExrMultipartHeader multipartHeader = new ExrMultipartHeader(sourceHeaders);

        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryWriteMultipartImages(sourceImages, sourceHeaders, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.SaveEXRMultipartImageToMemory(multipartImage, multipartHeader, out byte[] encoded));

        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            Assert.AreEqual(2, reader.NumParts);
            Assert.AreEqual(V3.PartType.Scanline, reader.GetHeader(0).PartType);
            Assert.AreEqual(V3.PartType.Tiled, reader.GetHeader(1).PartType);
            AssertPartHasCompressedZstdBlock(encoded, reader, 0);
            AssertPartHasCompressedZstdBlock(encoded, reader, 1);
        }

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRMultipartHeaderFromMemory(encoded, out ExrVersion version, out ExrMultipartHeader decodedHeaders));
        Assert.IsTrue(version.Multipart);
        Assert.AreEqual("scan-zstd", decodedHeaders.Headers[0].Name);
        Assert.AreEqual("scanlineimage", decodedHeaders.Headers[0].PartType);
        Assert.AreEqual("tile-zstd", decodedHeaders.Headers[1].Name);
        Assert.AreEqual("tiledimage", decodedHeaders.Headers[1].PartType);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRMultipartImageFromMemory(encoded, decodedHeaders, out ExrMultipartImage decodedImages));
        CollectionAssert.AreEqual(scanData, decodedImages.Images[0].GetChannel("R").Data);
        CollectionAssert.AreEqual(
            tileImage.GetChannel("R").Data,
            decodedImages.Images[1].GetChannel("R").Data);
        Assert.IsTrue(decodedImages.Images[1].Levels[0].Tiles.Count > 0);

        string path = Path.Combine(Path.GetTempPath(), $"TinyEXRNet-V1MultipartSave-{Guid.NewGuid():N}.exr");
        try
        {
            Assert.AreEqual(
                ResultCode.Success,
                Exr.SaveEXRMultipartImageToFile(multipartImage, multipartHeader, path));
            Assert.AreEqual(
                ResultCode.Success,
                Exr.ParseEXRMultipartHeaderFromFile(path, out _, out ExrMultipartHeader fileHeaders));
            Assert.AreEqual(
                ResultCode.Success,
                Exr.LoadEXRMultipartImageFromFile(path, fileHeaders, out ExrMultipartImage fileImages));
            CollectionAssert.AreEqual(scanData, fileImages.Images[0].GetChannel("R").Data);
            CollectionAssert.AreEqual(
                tileImage.GetChannel("R").Data,
                fileImages.Images[1].GetChannel("R").Data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 save facade converts sampled ZSTD channels through V3")]
    public void Case_V1SaveFacade_ConvertsSampledZstdChannelsThroughV3()
    {
        const int width = 128;
        const int height = 32;
        const int sampledCount = (width / 2) * (height / 2);
        const int fullCount = width * height;
        float[] uintSourceValues = { 0.0f, 1.9f, 42.75f, 65535.0f, 16777216.0f, 4294967040.0f };
        ExrImage image = new ExrImage(
            width,
            height,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("FtoH", ExrPixelType.Half, 2, 2, 1),
                    ExrPixelType.Float,
                    CreateRepeatedFloatBytes(HalfValues, sampledCount)),
                new ExrImageChannel(
                    new ExrChannel("FtoU", ExrPixelType.UInt),
                    ExrPixelType.Float,
                    CreateRepeatedFloatBytes(uintSourceValues, fullCount)),
                new ExrImageChannel(
                    new ExrChannel("HtoF", ExrPixelType.Float, 2, 2, 1),
                    ExrPixelType.Half,
                    CreateRepeatedHalfBytes(HalfBits, sampledCount)),
                new ExrImageChannel(
                    new ExrChannel("UtoF", ExrPixelType.Float),
                    ExrPixelType.UInt,
                    CreateRepeatedUIntBytes(CreateUIntValues(), fullCount)),
            });
        ExrHeader header = new ExrHeader
        {
            Compression = CompressionType.ZSTD,
            DataWindow = new ExrBox2i(-4, 6, 123, 37),
            DisplayWindow = new ExrBox2i(-4, 6, 123, 37),
        };

        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryWriteImage(image, header, out _));
        Assert.IsTrue(
            V1FacadeAdapter.TryWriteFlatImage(
                image,
                header,
                out ResultCode adapterResult,
                out byte[] adapterEncoded),
            "The sampled conversion shape should be eligible for the V3 writer bridge.");
        Assert.AreEqual(ResultCode.Success, adapterResult);
        Assert.IsTrue(adapterEncoded.Length > 0);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            AssertPartHasCompressedZstdBlock(encoded, reader, 0);
        }

        Assert.AreEqual(
            ResultCode.Success,
            Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader decodedHeader));
        Assert.AreEqual(4, decodedHeader.Channels.Count);
        Assert.AreEqual(2, decodedHeader.Channels[0].SamplingX);
        Assert.AreEqual(2, decodedHeader.Channels[0].SamplingY);
        Assert.AreEqual(2, decodedHeader.Channels[2].SamplingX);
        Assert.AreEqual(2, decodedHeader.Channels[2].SamplingY);
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadEXRImageFromMemory(encoded, decodedHeader, out ExrImage decodedImage));

        CollectionAssert.AreEqual(
            CreateRepeatedHalfBytes(HalfBits, sampledCount),
            decodedImage.GetChannel("FtoH").Data);
        uint[] expectedUInt = new uint[uintSourceValues.Length];
        for (int index = 0; index < uintSourceValues.Length; index++)
        {
            expectedUInt[index] = (uint)uintSourceValues[index];
        }

        CollectionAssert.AreEqual(
            CreateRepeatedUIntBytes(expectedUInt, fullCount),
            decodedImage.GetChannel("FtoU").Data);
        CollectionAssert.AreEqual(
            CreateRepeatedFloatBytes(HalfValues, sampledCount),
            decodedImage.GetChannel("HtoF").Data);
        float[] expectedFloat = CreateUIntValues().Select(static value => (float)value).ToArray();
        CollectionAssert.AreEqual(
            CreateRepeatedFloatBytes(expectedFloat, fullCount),
            decodedImage.GetChannel("UtoF").Data);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 deep facade delegates ZSTD memory stream and file reads to V3")]
    public void Case_V1DeepFacade_DelegatesZstdMemoryStreamAndFileReadsToV3()
    {
        V3.Box2i dataWindow = new V3.Box2i(-3, 5, 124, 36);
        V3.Chromaticities chromaticities = new V3.Chromaticities(
            0.64f,
            0.33f,
            0.30f,
            0.60f,
            0.15f,
            0.06f,
            0.3127f,
            0.3290f);
        byte[] customValue = Encoding.UTF8.GetBytes("v3-deep-core\0");
        V3.Header sourceHeader = new V3.Header(
            V3.PartType.DeepScanline,
            dataWindow,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt),
            },
            compression: V3.Compression.ZSTD,
            chromaticities: chromaticities,
            attributes: new[]
            {
                new V3.HeaderAttribute("facadeSource", "string", customValue),
            });
        byte[] encoded = WriteDeepImage(sourceHeader);

        using (V3.ExrReader reader = V3.ExrReader.OpenMemory(encoded))
        {
            Assert.AreEqual(V3.ExrResult.Success, reader.ParseHeader().Status);
            Assert.AreEqual(V3.Compression.ZSTD, reader.GetHeader(0).Compression);
            Assert.IsTrue(
                Enumerable.Range(0, reader.GetNumBlocks(0)).Any(blockIndex =>
                {
                    V3.BlockInfo block = reader.GetBlockInfo(0, blockIndex);
                    int chunkOffset = checked((int)block.FileOffset);
                    long packedSampleSize = BinaryPrimitives.ReadInt64LittleEndian(
                        encoded.AsSpan(chunkOffset + 12, sizeof(long)));
                    long unpackedSampleSize = BinaryPrimitives.ReadInt64LittleEndian(
                        encoded.AsSpan(chunkOffset + 20, sizeof(long)));
                    return packedSampleSize < unpackedSampleSize;
                }),
                "At least one deep sample payload should be a genuine ZSTD frame rather than a raw fallback.");
        }

        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            PortV1.ExrImplementation.TryReadDeepImage(encoded, out _, out _));
        Assert.AreEqual(
            ResultCode.Success,
            Exr.TryReadDeepImage(encoded, out ExrHeader memoryHeader, out ExrDeepImage memoryImage));
        AssertDeepFacade(sourceHeader, customValue, memoryHeader, memoryImage);

        const int origin = 29;
        byte[] container = new byte[checked(origin + encoded.Length)];
        encoded.CopyTo(container, origin);
        using MemoryStream stream = new MemoryStream(container, writable: false);
        stream.Position = origin;
        Assert.AreEqual(
            ResultCode.Success,
            Exr.LoadDeepEXRFromStream(stream, out ExrHeader streamHeader, out ExrDeepImage streamImage));
        Assert.AreEqual(origin, stream.Position);
        ExrTestHelper.EqualHeaders(memoryHeader, streamHeader);
        AssertDeepFacade(sourceHeader, customValue, streamHeader, streamImage);

        string path = Path.Combine(Path.GetTempPath(), $"TinyEXRNet-V1Deep-{Guid.NewGuid():N}.exr");
        try
        {
            File.WriteAllBytes(path, encoded);
            Assert.AreEqual(
                ResultCode.Success,
                Exr.LoadDeepEXR(path, out ExrHeader fileHeader, out ExrDeepImage fileImage));
            ExrTestHelper.EqualHeaders(memoryHeader, fileHeader);
            AssertDeepFacade(sourceHeader, customValue, fileHeader, fileImage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 deep facade reads one-level deep tiles and rejects multiple levels")]
    public void Case_V1DeepFacade_HandlesRepresentableDeepTiledShape()
    {
        V3.Box2i dataWindow = new(-3, 5, 4, 10);
        V3.Header oneLevel = new(
            V3.PartType.DeepTiled,
            dataWindow,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt),
            },
            compression: V3.Compression.ZSTD,
            tiles: new V3.TileDescription(3, 2),
            attributes: new[]
            {
                new V3.HeaderAttribute("facadeSource", "string", Encoding.UTF8.GetBytes("deep-tile\0")),
            });
        byte[] encoded = WriteDeepImage(oneLevel);

        Assert.AreEqual(
            ResultCode.Success,
            Exr.TryReadDeepImage(encoded, out ExrHeader header, out ExrDeepImage image));
        Assert.IsTrue(header.IsDeep);
        Assert.AreEqual("deeptile", header.PartType);
        Assert.IsNotNull(header.Tiles);
        Assert.AreEqual(ExrTileLevelMode.OneLevel, header.Tiles!.LevelMode);
        Assert.AreEqual(dataWindow.Width, image.Width);
        Assert.AreEqual(dataWindow.Height, image.Height);
        AssertDeepPixels(oneLevel, image);

        V3.Header mip = new(
            V3.PartType.DeepTiled,
            dataWindow,
            oneLevel.Channels,
            compression: V3.Compression.ZSTD,
            tiles: new V3.TileDescription(
                3,
                2,
                V3.TileLevelMode.MipmapLevels,
                V3.TileRoundingMode.RoundDown));
        Assert.AreEqual(
            ResultCode.UnsupportedFeature,
            Exr.TryReadDeepImage(WriteDeepImage(mip), out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V1 multipart adapter explicitly rejects mixed flat and deep parts")]
    public void Case_V1MultipartAdapter_ExplicitlyRejectsMixedFlatAndDeepParts()
    {
        V3.Header flat = new(
            V3.PartType.Scanline,
            DataWindow,
            new[]
            {
                new V3.Channel("F", V3.PixelType.Float),
                new V3.Channel("H", V3.PixelType.Half),
                new V3.Channel("U", V3.PixelType.UInt),
            },
            compression: V3.Compression.ZSTD,
            name: "flat");
        V3.Header deep = new(
            V3.PartType.DeepScanline,
            DataWindow,
            flat.Channels,
            compression: V3.Compression.ZSTD,
            name: "deep");
        byte[] encoded = WriteMixedMultipartImage(flat, deep);

        Assert.IsTrue(V1FacadeAdapter.TryParseMultipartHeaders(
            encoded,
            out ResultCode parseResult,
            out ExrVersion version,
            out ExrHeader[] headers));
        Assert.AreEqual(ResultCode.Success, parseResult);
        Assert.IsTrue(version.Multipart);
        Assert.AreEqual(2, headers.Length);

        Assert.IsTrue(V1FacadeAdapter.TryReadMultipartImages(
            encoded,
            headers,
            out ResultCode memoryResult,
            out ExrImage[] memoryImages));
        Assert.AreEqual(ResultCode.UnsupportedFeature, memoryResult);
        Assert.AreEqual(0, memoryImages.Length);

        const int origin = 19;
        byte[] container = new byte[checked(origin + encoded.Length)];
        encoded.CopyTo(container, origin);
        using MemoryStream stream = new MemoryStream(container, writable: false);
        stream.Position = origin;
        Assert.IsTrue(V1FacadeAdapter.TryReadMultipartImages(
            stream,
            headers,
            out ResultCode streamResult,
            out ExrImage[] streamImages));
        Assert.AreEqual(ResultCode.UnsupportedFeature, streamResult);
        Assert.AreEqual(0, streamImages.Length);
        Assert.AreEqual(origin, stream.Position);
    }

    private static byte[] WriteImage(V3.Header header)
    {
        using MemoryStream stream = new MemoryStream();
        using V3IO.StreamDataSink sink = new V3IO.StreamDataSink(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        Assert.AreEqual(0, writer.AddPart(header));
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = writer.GetBlockInfo(0, blockIndex);
            V3.WriterResult writeResult = writer.WriteScanlineBlock(
                0,
                block.Region.MinY,
                CreateBlockChannels(header, block.Region));
            Assert.AreEqual(V3.ExrResult.Success, writeResult.Status, writeResult.Error?.ToString());
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static byte[] WriteDeepImage(V3.Header header)
    {
        using MemoryStream stream = new MemoryStream();
        using V3IO.StreamDataSink sink = new V3IO.StreamDataSink(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        Assert.AreEqual(0, writer.AddPart(header));
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = writer.GetBlockInfo(0, blockIndex);
            int[] sampleCounts = CreateDeepSampleCounts(block.Region);
            IReadOnlyList<V3.ChannelBuffer> channels = CreateDeepChannels(
                header,
                block.Region,
                sampleCounts);
            V3.WriterResult result = header.IsTiled
                ? writer.WriteDeepTile(
                    0,
                    block.TileX,
                    block.TileY,
                    block.LevelX,
                    block.LevelY,
                    sampleCounts,
                    channels)
                : writer.WriteDeepScanlineBlock(
                    0,
                    block.Region.MinY,
                    sampleCounts,
                    channels);
            Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static byte[] WriteMixedMultipartImage(V3.Header flat, V3.Header deep)
    {
        V3.Header[] headers = { flat, deep };
        using MemoryStream stream = new MemoryStream();
        using V3IO.StreamDataSink sink = new V3IO.StreamDataSink(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        for (int partIndex = 0; partIndex < headers.Length; partIndex++)
        {
            Assert.AreEqual(partIndex, writer.AddPart(headers[partIndex]));
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int partIndex = 0; partIndex < headers.Length; partIndex++)
        {
            V3.Header header = headers[partIndex];
            for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(partIndex); blockIndex++)
            {
                V3.BlockInfo block = writer.GetBlockInfo(partIndex, blockIndex);
                V3.WriterResult result;
                if (header.IsDeep)
                {
                    int[] sampleCounts = CreateDeepSampleCounts(block.Region);
                    result = writer.WriteDeepScanlineBlock(
                        partIndex,
                        block.Region.MinY,
                        sampleCounts,
                        CreateDeepChannels(header, block.Region, sampleCounts));
                }
                else
                {
                    result = writer.WriteScanlineBlock(
                        partIndex,
                        block.Region.MinY,
                        CreateBlockChannels(header, block.Region));
                }

                Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            }
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static byte[] WriteTiledImage(V3.Header header, byte[] levelData)
    {
        using MemoryStream stream = new MemoryStream();
        using V3IO.StreamDataSink sink = new V3IO.StreamDataSink(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        Assert.AreEqual(0, writer.AddPart(header));
        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(0); blockIndex++)
        {
            V3.BlockInfo block = writer.GetBlockInfo(0, blockIndex);
            byte[] tileData = SliceFloatPlane(
                levelData,
                checked((int)header.DataWindow.Width),
                block.Region.MinX - header.DataWindow.MinX,
                block.Region.MinY - header.DataWindow.MinY,
                checked((int)block.Region.Width),
                checked((int)block.Region.Height));
            Assert.AreEqual(
                V3.ExrResult.Success,
                writer.WriteTile(
                    0,
                    block.TileX,
                    block.TileY,
                    block.LevelX,
                    block.LevelY,
                    new[] { new V3.ChannelBuffer("R", V3.PixelType.Float, tileData) }).Status);
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static byte[] WriteMultipartImage(
        IReadOnlyList<V3.Header> headers,
        IReadOnlyList<byte[]> levelData)
    {
        Assert.AreEqual(headers.Count, levelData.Count);
        using MemoryStream stream = new MemoryStream();
        using V3IO.StreamDataSink sink = new V3IO.StreamDataSink(stream, leaveOpen: true);
        using V3.ExrWriter writer = V3.ExrWriter.OpenSink(sink);
        for (int partIndex = 0; partIndex < headers.Count; partIndex++)
        {
            Assert.AreEqual(partIndex, writer.AddPart(headers[partIndex]));
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.Begin().Status);
        for (int partIndex = 0; partIndex < headers.Count; partIndex++)
        {
            V3.Header header = headers[partIndex];
            int levelWidth = checked((int)header.DataWindow.Width);
            for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(partIndex); blockIndex++)
            {
                V3.BlockInfo block = writer.GetBlockInfo(partIndex, blockIndex);
                byte[] blockData = SliceFloatPlane(
                    levelData[partIndex],
                    levelWidth,
                    block.Region.MinX - header.DataWindow.MinX,
                    block.Region.MinY - header.DataWindow.MinY,
                    checked((int)block.Region.Width),
                    checked((int)block.Region.Height));
                V3.Channel channel = header.Channels[0];
                V3.ChannelBuffer[] channels =
                {
                    new V3.ChannelBuffer(channel.Name, channel.PixelType, blockData),
                };
                V3.WriterResult writeResult = header.IsTiled
                    ? writer.WriteTile(
                        partIndex,
                        block.TileX,
                        block.TileY,
                        block.LevelX,
                        block.LevelY,
                        channels)
                    : writer.WriteScanlineBlock(
                        partIndex,
                        block.Region.MinY,
                        channels);
                Assert.AreEqual(V3.ExrResult.Success, writeResult.Status);
            }
        }

        Assert.AreEqual(V3.ExrResult.Success, writer.End().Status);
        return stream.ToArray();
    }

    private static IReadOnlyList<V3.ChannelBuffer> CreateBlockChannels(
        V3.Header header,
        V3.Box2i region)
    {
        List<V3.ChannelBuffer> channels = new List<V3.ChannelBuffer>(header.Channels.Count);
        foreach (V3.Channel channel in header.Channels)
        {
            int sampleCount = checked((int)(region.Width * region.Height));
            byte[] data = new byte[checked(sampleCount * (channel.PixelType == V3.PixelType.Half ? 2 : 4))];
            int destinationOffset = 0;
            for (int y = region.MinY; y <= region.MaxY; y++)
            {
                for (int x = region.MinX; x <= region.MaxX; x++)
                {
                    int sourceIndex = checked((y - DataWindow.MinY) * (int)DataWindow.Width + x - DataWindow.MinX);
                    switch (channel.Name)
                    {
                        case "F":
                            BinaryPrimitives.WriteInt32LittleEndian(
                                data.AsSpan(destinationOffset, sizeof(float)),
                                BitConverter.SingleToInt32Bits(CreateFloatValues()[sourceIndex]));
                            destinationOffset += sizeof(float);
                            break;
                        case "H":
                            BinaryPrimitives.WriteUInt16LittleEndian(
                                data.AsSpan(destinationOffset, sizeof(ushort)),
                                HalfBits[sourceIndex]);
                            destinationOffset += sizeof(ushort);
                            break;
                        case "U":
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                data.AsSpan(destinationOffset, sizeof(uint)),
                                CreateUIntValues()[sourceIndex]);
                            destinationOffset += sizeof(uint);
                            break;
                        default:
                            Assert.Fail($"Unexpected channel '{channel.Name}'.");
                            break;
                    }
                }
            }

            Assert.AreEqual(data.Length, destinationOffset);
            channels.Add(new V3.ChannelBuffer(channel.Name, channel.PixelType, data));
        }

        return channels;
    }

    private static int[] CreateDeepSampleCounts(V3.Box2i region)
    {
        int[] result = new int[checked((int)(region.Width * region.Height))];
        int target = 0;
        for (int y = region.MinY; y <= region.MaxY; y++)
        {
            for (int x = region.MinX; x <= region.MaxX; x++)
            {
                result[target++] = GetDeepSampleCount(x, y);
            }
        }

        Assert.AreEqual(result.Length, target);
        return result;
    }

    private static IReadOnlyList<V3.ChannelBuffer> CreateDeepChannels(
        V3.Header header,
        V3.Box2i region,
        ReadOnlySpan<int> sampleCounts)
    {
        int totalSamples = 0;
        for (int index = 0; index < sampleCounts.Length; index++)
        {
            totalSamples = checked(totalSamples + sampleCounts[index]);
        }

        List<V3.ChannelBuffer> channels = new List<V3.ChannelBuffer>(header.Channels.Count);
        foreach (V3.Channel channel in header.Channels)
        {
            int elementSize = channel.PixelType == V3.PixelType.Half ? sizeof(ushort) : sizeof(uint);
            byte[] data = new byte[checked(totalSamples * elementSize)];
            int pixelIndex = 0;
            int target = 0;
            for (int y = region.MinY; y <= region.MaxY; y++)
            {
                for (int x = region.MinX; x <= region.MaxX; x++)
                {
                    int sampleCount = sampleCounts[pixelIndex++];
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        int valueIndex = GetDeepValueIndex(x, y, sampleIndex);
                        switch (channel.PixelType)
                        {
                            case V3.PixelType.UInt:
                                BinaryPrimitives.WriteUInt32LittleEndian(
                                    data.AsSpan(target, sizeof(uint)),
                                    CreateUIntValues()[valueIndex]);
                                break;
                            case V3.PixelType.Half:
                                BinaryPrimitives.WriteUInt16LittleEndian(
                                    data.AsSpan(target, sizeof(ushort)),
                                    HalfBits[valueIndex]);
                                break;
                            case V3.PixelType.Float:
                                BinaryPrimitives.WriteInt32LittleEndian(
                                    data.AsSpan(target, sizeof(float)),
                                    BitConverter.SingleToInt32Bits(CreateFloatValues()[valueIndex]));
                                break;
                            default:
                                Assert.Fail($"Unexpected deep channel type '{channel.PixelType}'.");
                                break;
                        }

                        target += elementSize;
                    }
                }
            }

            Assert.AreEqual(data.Length, target);
            channels.Add(new V3.ChannelBuffer(channel.Name, channel.PixelType, data));
        }

        return channels;
    }

    private static void AssertDeepFacade(
        V3.Header sourceHeader,
        byte[] expectedCustomValue,
        ExrHeader header,
        ExrDeepImage image)
    {
        Assert.IsTrue(header.IsDeep);
        Assert.IsFalse(header.IsMultipart);
        Assert.AreEqual("deepscanline", header.PartType);
        Assert.AreEqual(CompressionType.ZSTD, header.Compression);
        Assert.AreEqual(sourceHeader.DataWindow.MinX, header.DataWindow.MinX);
        Assert.AreEqual(sourceHeader.DataWindow.MinY, header.DataWindow.MinY);
        Assert.AreEqual(sourceHeader.DataWindow.MaxX, header.DataWindow.MaxX);
        Assert.AreEqual(sourceHeader.DataWindow.MaxY, header.DataWindow.MaxY);
        Assert.AreEqual(sourceHeader.DataWindow.Width, image.Width);
        Assert.AreEqual(sourceHeader.DataWindow.Height, image.Height);
        CollectionAssert.AreEqual(
            new[] { "F", "H", "U" },
            header.Channels.Select(static channel => channel.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "F", "H", "U" },
            image.Channels.Select(static channel => channel.Name).ToArray());

        ExrAttribute version = header.CustomAttributes.Single(
            static attribute => attribute.Name == "version");
        Assert.AreEqual("int", version.TypeName);
        Assert.AreEqual(1, version.ReadInt32());
        ExrAttribute maximumSamples = header.CustomAttributes.Single(
            static attribute => attribute.Name == "maxSamplesPerPixel");
        Assert.AreEqual("int", maximumSamples.TypeName);
        Assert.AreEqual(2, maximumSamples.ReadInt32());
        ExrAttribute custom = header.CustomAttributes.Single(
            static attribute => attribute.Name == "facadeSource");
        Assert.AreEqual("string", custom.TypeName);
        CollectionAssert.AreEqual(expectedCustomValue, custom.Value);
        ExrAttribute chromaticities = header.CustomAttributes.Single(
            static attribute => attribute.Name == "chromaticities");
        Assert.AreEqual("chromaticities", chromaticities.TypeName);
        float[] expectedChromaticities =
        {
            0.64f,
            0.33f,
            0.30f,
            0.60f,
            0.15f,
            0.06f,
            0.3127f,
            0.3290f,
        };
        Assert.AreEqual(expectedChromaticities.Length * sizeof(float), chromaticities.Value.Length);
        for (int index = 0; index < expectedChromaticities.Length; index++)
        {
            Assert.AreEqual(expectedChromaticities[index], chromaticities.ReadSingle(index * sizeof(float)));
        }

        int rowIndex = 0;
        for (int y = sourceHeader.DataWindow.MinY; y <= sourceHeader.DataWindow.MaxY; y++, rowIndex++)
        {
            int cumulative = 0;
            Dictionary<string, List<float>> expectedRows = sourceHeader.Channels.ToDictionary(
                static channel => channel.Name,
                static _ => new List<float>(),
                StringComparer.Ordinal);
            int columnIndex = 0;
            for (int x = sourceHeader.DataWindow.MinX; x <= sourceHeader.DataWindow.MaxX; x++, columnIndex++)
            {
                int sampleCount = GetDeepSampleCount(x, y);
                cumulative = checked(cumulative + sampleCount);
                Assert.AreEqual(cumulative, image.OffsetTable[rowIndex][columnIndex]);
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    foreach (V3.Channel channel in sourceHeader.Channels)
                    {
                        expectedRows[channel.Name].Add(
                            GetDeepSampleAsFloat(channel.PixelType, x, y, sampleIndex));
                    }
                }
            }

            foreach (ExrDeepChannel channel in image.Channels)
            {
                CollectionAssert.AreEqual(expectedRows[channel.Name].ToArray(), channel.Rows[rowIndex]);
            }
        }

        Assert.AreEqual(image.Height, rowIndex);
    }

    private static void AssertDeepPixels(V3.Header sourceHeader, ExrDeepImage image)
    {
        int rowIndex = 0;
        for (int y = sourceHeader.DataWindow.MinY; y <= sourceHeader.DataWindow.MaxY; y++, rowIndex++)
        {
            int cumulative = 0;
            for (int xIndex = 0; xIndex < image.Width; xIndex++)
            {
                int x = sourceHeader.DataWindow.MinX + xIndex;
                int count = GetDeepSampleCount(x, y);
                cumulative += count;
                Assert.AreEqual(cumulative, image.OffsetTable[rowIndex][xIndex]);
            }

            foreach (ExrDeepChannel channel in image.Channels)
            {
                Assert.AreEqual(cumulative, channel.Rows[rowIndex].Length, channel.Name);
            }
        }
    }

    private static int GetDeepSampleCount(int x, int y)
    {
        uint selector = unchecked((uint)((x * 3) + (y * 5)));
        return (selector & 7U) == 0U ? 0 : (selector & 3U) == 0U ? 2 : 1;
    }

    private static int GetDeepValueIndex(int x, int y, int sampleIndex)
    {
        uint seed = unchecked((uint)((x * 17) + (y * 31) + (sampleIndex * 53)));
        return (int)(seed % (uint)HalfBits.Length);
    }

    private static float GetDeepSampleAsFloat(
        V3.PixelType pixelType,
        int x,
        int y,
        int sampleIndex)
    {
        int valueIndex = GetDeepValueIndex(x, y, sampleIndex);
        return pixelType switch
        {
            V3.PixelType.UInt => CreateUIntValues()[valueIndex],
            V3.PixelType.Half => HalfValues[valueIndex],
            V3.PixelType.Float => CreateFloatValues()[valueIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(pixelType)),
        };
    }

    private static ExrImage CreateV1TiledImage(
        int width,
        int height,
        ExrTileLevelMode levelMode,
        ExrTileRoundingMode roundingMode)
    {
        List<ExrImageLevel> levels = new List<ExrImageLevel>();
        int xLevelCount = GetV1LevelCount(width, roundingMode);
        int yLevelCount = GetV1LevelCount(height, roundingMode);
        if (levelMode == ExrTileLevelMode.RipMapLevels)
        {
            for (int levelY = 0; levelY < yLevelCount; levelY++)
            {
                for (int levelX = 0; levelX < xLevelCount; levelX++)
                {
                    levels.Add(CreateV1TiledLevel(
                        width,
                        height,
                        levelX,
                        levelY,
                        roundingMode));
                }
            }
        }
        else
        {
            int levelCount = levelMode == ExrTileLevelMode.OneLevel
                ? 1
                : Math.Max(xLevelCount, yLevelCount);
            for (int level = 0; level < levelCount; level++)
            {
                int levelX = levelMode == ExrTileLevelMode.OneLevel ? 0 : level;
                int levelY = levelMode == ExrTileLevelMode.OneLevel ? 0 : level;
                levels.Add(CreateV1TiledLevel(
                    width,
                    height,
                    levelX,
                    levelY,
                    roundingMode));
            }
        }

        return new ExrImage(levels);
    }

    private static ExrImageLevel CreateV1TiledLevel(
        int baseWidth,
        int baseHeight,
        int levelX,
        int levelY,
        ExrTileRoundingMode roundingMode)
    {
        int width = GetV1LevelSize(baseWidth, levelX, roundingMode);
        int height = GetV1LevelSize(baseHeight, levelY, roundingMode);
        byte[] data = new byte[checked(width * height * sizeof(float))];
        for (int pixelIndex = 0; pixelIndex < width * height; pixelIndex++)
        {
            float value = ((pixelIndex + (levelX * 7) + (levelY * 11)) % 64) * 0.25f;
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(pixelIndex * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(value));
        }

        return new ExrImageLevel(
            levelX,
            levelY,
            width,
            height,
            new[]
            {
                new ExrImageChannel(
                    new ExrChannel("R", ExrPixelType.Float),
                    ExrPixelType.Float,
                    data),
            });
    }

    private static int GetV1LevelCount(int size, ExrTileRoundingMode roundingMode)
    {
        int count = 1;
        while (size > 1)
        {
            size = roundingMode == ExrTileRoundingMode.RoundUp
                ? (size + 1) / 2
                : size / 2;
            count++;
        }

        return count;
    }

    private static int GetV1LevelSize(
        int size,
        int level,
        ExrTileRoundingMode roundingMode)
    {
        for (int levelIndex = 0; levelIndex < level; levelIndex++)
        {
            size = roundingMode == ExrTileRoundingMode.RoundUp
                ? (size + 1) / 2
                : Math.Max(size / 2, 1);
        }

        return Math.Max(size, 1);
    }

    private static ExrAttribute CreateChromaticitiesAttribute()
    {
        float[] values =
        {
            0.64f,
            0.33f,
            0.30f,
            0.60f,
            0.15f,
            0.06f,
            0.3127f,
            0.3290f,
        };
        byte[] data = new byte[values.Length * sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[index]));
        }

        return new ExrAttribute("chromaticities", "chromaticities", data);
    }

    private static void AssertPartHasCompressedZstdBlock(
        byte[] encoded,
        V3.ExrReader reader,
        int partIndex)
    {
        Assert.IsTrue(
            Enumerable.Range(0, reader.GetNumBlocks(partIndex)).Any(blockIndex =>
            {
                V3.BlockInfo block = reader.GetBlockInfo(partIndex, blockIndex);
                int chunkOffset = checked((int)block.FileOffset);
                int packedSize = BinaryPrimitives.ReadInt32LittleEndian(
                    encoded.AsSpan(
                        chunkOffset + block.ChunkHeaderByteCount - sizeof(int),
                        sizeof(int)));
                return packedSize >= 0 && (ulong)packedSize < block.UncompressedByteCount!.Value;
            }),
            $"Part {partIndex} should contain at least one genuine ZSTD block.");
    }

    private static float[] CreateFloatValues()
    {
        return new[] { -10.25f, 0.0f, 1.5f, 8.0f, 16.75f, 1024.5f };
    }

    private static byte[] CreateFloatBytes()
    {
        float[] values = CreateFloatValues();
        byte[] data = new byte[values.Length * sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[index]));
        }

        return data;
    }

    private static uint[] CreateUIntValues()
    {
        return new uint[] { 0, 1, 42, 65_535, 0x80000000, uint.MaxValue };
    }

    private static byte[] CreateRepeatingFloatBytes(int sampleCount)
    {
        byte[] data = new byte[checked(sampleCount * sizeof(float))];
        for (int index = 0; index < sampleCount; index++)
        {
            float value = (index % 64) * 0.25f;
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(value));
        }

        return data;
    }

    private static byte[] CreateRepeatedFloatBytes(IReadOnlyList<float> values, int sampleCount)
    {
        byte[] data = new byte[checked(sampleCount * sizeof(float))];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(sampleIndex * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[sampleIndex % values.Count]));
        }

        return data;
    }

    private static byte[] CreateRepeatedHalfBytes(IReadOnlyList<ushort> values, int sampleCount)
    {
        byte[] data = new byte[checked(sampleCount * sizeof(ushort))];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(sampleIndex * sizeof(ushort), sizeof(ushort)),
                values[sampleIndex % values.Count]);
        }

        return data;
    }

    private static byte[] CreateRepeatedUIntBytes(IReadOnlyList<uint> values, int sampleCount)
    {
        byte[] data = new byte[checked(sampleCount * sizeof(uint))];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(sampleIndex * sizeof(uint), sizeof(uint)),
                values[sampleIndex % values.Count]);
        }

        return data;
    }

    private static ExrImageChannel FloatImageChannel(
        string name,
        int width,
        int height,
        float value)
    {
        byte[] data = new byte[checked(width * height * sizeof(float))];
        for (int pixelIndex = 0; pixelIndex < width * height; pixelIndex++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(pixelIndex * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(value));
        }

        return new ExrImageChannel(
            new ExrChannel(name, ExrPixelType.Float),
            ExrPixelType.Float,
            data);
    }

    private static void AssertRgba(
        float[] rgba,
        float expectedR,
        float expectedG,
        float expectedB,
        float expectedA)
    {
        Assert.AreEqual(0, rgba.Length % 4);
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            Assert.AreEqual(expectedR, rgba[offset + 0]);
            Assert.AreEqual(expectedG, rgba[offset + 1]);
            Assert.AreEqual(expectedB, rgba[offset + 2]);
            Assert.AreEqual(expectedA, rgba[offset + 3]);
        }
    }

    private static void AssertTile(
        ExrTile tile,
        int offsetX,
        int offsetY,
        int width,
        int height,
        byte[] levelData,
        int levelWidth)
    {
        Assert.AreEqual(offsetX, tile.OffsetX);
        Assert.AreEqual(offsetY, tile.OffsetY);
        Assert.AreEqual(width, tile.Width);
        Assert.AreEqual(height, tile.Height);
        Assert.AreEqual(0, tile.LevelX);
        Assert.AreEqual(0, tile.LevelY);
        Assert.AreEqual(1, tile.Channels.Count);
        CollectionAssert.AreEqual(
            SliceFloatPlane(levelData, levelWidth, offsetX, offsetY, width, height),
            tile.Channels[0].Data);
    }

    private static byte[] SliceFloatPlane(
        byte[] source,
        int sourceWidth,
        int offsetX,
        int offsetY,
        int width,
        int height)
    {
        int rowByteCount = checked(width * sizeof(float));
        byte[] result = new byte[checked(rowByteCount * height)];
        for (int row = 0; row < height; row++)
        {
            int sourceOffset = checked(((offsetY + row) * sourceWidth + offsetX) * sizeof(float));
            source.AsSpan(sourceOffset, rowByteCount).CopyTo(
                result.AsSpan(row * rowByteCount, rowByteCount));
        }

        return result;
    }

    private static float[] ReadFloats(byte[] data)
    {
        float[] values = new float[data.Length / sizeof(float)];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(index * sizeof(float), sizeof(float))));
        }

        return values;
    }

    private static uint[] ReadUInts(byte[] data)
    {
        uint[] values = new uint[data.Length / sizeof(uint)];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(index * sizeof(uint), sizeof(uint)));
        }

        return values;
    }
}
