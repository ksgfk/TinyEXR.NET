using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TinyEXR.Test;

public static class ExrTestData
{
    private static readonly string[] ScanlineImages =
    {
        "ScanLines/Blobbies.exr",
        "ScanLines/CandleGlass.exr",
        "ScanLines/Desk.exr",
        "ScanLines/MtTamWest.exr",
        "ScanLines/PrismsLenses.exr",
        "ScanLines/StillLife.exr",
        "ScanLines/Tree.exr",
    };

    private static readonly string[] ChromaticityImages =
    {
        "Chromaticities/Rec709.exr",
        "Chromaticities/Rec709_YC.exr",
        "Chromaticities/XYZ.exr",
        "Chromaticities/XYZ_YC.exr",
    };

    private static readonly string[] TestImages =
    {
        "TestImages/AllHalfValues.exr",
        "TestImages/BrightRings.exr",
        "TestImages/BrightRingsNanInf.exr",
        "TestImages/WideColorGamut.exr",
    };

    private static readonly string[] LuminanceChromaImages =
    {
        "LuminanceChroma/MtTamNorth.exr",
        "LuminanceChroma/StarField.exr",
    };

    private static readonly string[] MultiResolutionImages =
    {
        "MultiResolution/Bonita.exr",
        "MultiResolution/Kapaa.exr",
    };

    private static readonly string[] MultipartCombineInputs =
    {
        "MultiResolution/Kapaa.exr",
        "Tiles/GoldenGate.exr",
        "ScanLines/Desk.exr",
        "MultiResolution/PeriodicPattern.exr",
    };

    private static readonly string[] FuzzedHeaderRegressionFiles =
    {
        "poc-eedff3a9e99eb1c0fd3a3b0989e7c44c0a69f04f10b23e5264f362a4773f4397_min",
        "poc-df76d1f27adb8927a1446a603028272140905c168a336128465a1162ec7af270.mini",
        "poc-1383755b301e5f505b2198dc0508918b537fdf48bbfc6deeffe268822e6f6cd6",
        "poc-3f1f642c3356fd8e8d2a0787613ec09a56572b3a1e38c9629b6db9e8dead1117_min",
        "poc-5b66774a7498c635334ad386be0c3b359951738ac47f14878a3346d1c6ea0fe5_min",
    };

    public static IEnumerable<object[]> SinglePartImageFiles()
    {
        foreach (string relativePath in ScanlineImages
            .Concat(ChromaticityImages)
            .Concat(TestImages)
            .Concat(LuminanceChromaImages)
            .Concat(Enumerable.Range(1, 16).Select(static i => $"DisplayWindow/t{i:00}.exr"))
            .Concat(Enumerable.Range(1, 8).Select(static i => $"Beachball/singlepart.{i:0000}.exr")))
        {
            yield return new object[] { relativePath };
        }
    }

    public static IEnumerable<object[]> ScanlineRoundTripFiles()
    {
        foreach (string relativePath in ScanlineImages)
        {
            yield return new object[] { relativePath };
        }
    }

    public static IEnumerable<object[]> MultiResolutionRoundTripFiles()
    {
        foreach (string relativePath in MultiResolutionImages)
        {
            yield return new object[] { relativePath };
        }
    }

    public static IEnumerable<object[]> MultipartFrames()
    {
        foreach (int index in Enumerable.Range(1, 8))
        {
            yield return new object[] { $"Beachball/multipart.{index:0000}.exr" };
        }
    }

    public static IEnumerable<object[]> MultipartCombineFiles()
    {
        foreach (string relativePath in MultipartCombineInputs)
        {
            yield return new object[] { relativePath };
        }
    }

    public static IEnumerable<object[]> SubsampledChromaImageFiles()
    {
        foreach (string relativePath in ChromaticityImages
            .Where(static path => path.EndsWith("_YC.exr", StringComparison.Ordinal))
            .Concat(LuminanceChromaImages))
        {
            yield return new object[] { relativePath };
        }
    }

    public static IEnumerable<object[]> FuzzedHeaderFiles()
    {
        foreach (string filename in FuzzedHeaderRegressionFiles)
        {
            yield return new object[] { filename };
        }
    }
}

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string OpenExrImagesRoot { get; } = EnsureDirectory(
        Path.Combine(RepoRoot, ".cache", "openexr-images"),
        "Run Scripts/prepare-openexr-images.ps1 before executing the test suite.");

    public static string NativeTinyExrRoot { get; } = EnsureDirectory(Path.Combine(RepoRoot, "TinyEXR.Native", "tinyexr"));

    public static string NativeUnitTestRoot { get; } = EnsureDirectory(Path.Combine(NativeTinyExrRoot, "test", "unit"));

    public static string RegressionRoot { get; } = EnsureDirectory(Path.Combine(NativeUnitTestRoot, "regression"));

    public static string NativeDataRoot { get; } = EnsureDirectory(Path.Combine(NativeTinyExrRoot, "data"));

    public static string OpenExr(string relativePath) => EnsureFile(CombineRelative(OpenExrImagesRoot, relativePath));

    public static string Regression(string fileName) => EnsureFile(Path.Combine(RegressionRoot, fileName));

    public static string Asakusa => EnsureFile(Path.Combine(NativeTinyExrRoot, "asakusa.exr"));

    public static string DeepScanline => EnsureFile(Path.Combine(NativeDataRoot, "deepscanline.exr"));

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "TinyEXR.NET")) &&
                Directory.Exists(Path.Combine(directory.FullName, "TinyEXR.Native")) &&
                File.Exists(Path.Combine(directory.FullName, "TinyEXR.NET.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Unable to locate repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string CombineRelative(string root, string relativePath)
    {
        string normalized = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.Combine(root, normalized);
    }

    private static string EnsureDirectory(string path, string? hint = null)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(hint == null ? path : $"{path}{Environment.NewLine}{hint}");
        }

        return path;
    }

    private static string EnsureFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required test file was not found: {path}", path);
        }

        return path;
    }
}

internal static class ExrTestHelper
{
    private const float Pxr24Tolerance = 0.01f;
    private const float B44Tolerance = 0.5f;

    private static readonly ConditionalWeakTable<ExrImage, ImageComparisonSettings> ImageComparisonSettingsByImage = new();
    private static readonly ConditionalWeakTable<ExrHeader, HeaderComparisonSettings> HeaderComparisonSettingsByHeader = new();

    public static (ExrVersion Version, ExrHeader Header, ExrImage Image) LoadSinglePart(string path)
    {
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(path, header, out ExrImage image));
        TrackHeader(header);
        TrackCompression(image, header.Compression);
        return (version, header, image);
    }

    public static (ExrVersion Version, ExrMultipartHeader Headers, ExrMultipartImage Images) LoadMultipart(string path)
    {
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out _, out ExrMultipartHeader headers));
        Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromFile(path, headers, out ExrMultipartImage images));
        Assert.AreEqual(headers.Headers.Count, images.Images.Count);
        for (int i = 0; i < headers.Headers.Count; i++)
        {
            TrackHeader(headers.Headers[i]);
            TrackCompression(images.Images[i], headers.Headers[i].Compression);
        }

        return (version, headers, images);
    }

    public static void EqualHeaders(ExrHeader expected, ExrHeader actual, bool ignoreMultipartState = false)
    {
        LineOrderType expectedLineOrder = HeaderComparisonSettingsByHeader.TryGetValue(expected, out HeaderComparisonSettings? settings)
            ? settings.EncodedLineOrder
            : expected.LineOrder;

        Assert.AreEqual(expected.Compression, actual.Compression);
        Assert.AreEqual(expectedLineOrder, actual.LineOrder);
        Assert.AreEqual(expected.PixelAspectRatio, actual.PixelAspectRatio);
        Assert.AreEqual(expected.ScreenWindowCenter.X, actual.ScreenWindowCenter.X);
        Assert.AreEqual(expected.ScreenWindowCenter.Y, actual.ScreenWindowCenter.Y);
        Assert.AreEqual(expected.ScreenWindowWidth, actual.ScreenWindowWidth);
        Assert.AreEqual(expected.Channels.Count, actual.Channels.Count);
        EqualBoxes(expected.DataWindow, actual.DataWindow, nameof(ExrHeader.DataWindow));
        EqualBoxes(expected.DisplayWindow, actual.DisplayWindow, nameof(ExrHeader.DisplayWindow));
        Assert.AreEqual(expected.Tiles is not null, actual.Tiles is not null);
        Assert.AreEqual(expected.Name, actual.Name);
        string? expectedPartType = expected.PartType;
        if (ignoreMultipartState && string.IsNullOrEmpty(expectedPartType))
        {
            expectedPartType = expected.Tiles is null ? "scanlineimage" : "tiledimage";
        }

        Assert.AreEqual(expectedPartType, actual.PartType);
        Assert.AreEqual(expected.IsDeep, actual.IsDeep);
        if (!ignoreMultipartState)
        {
            Assert.AreEqual(expected.IsMultipart, actual.IsMultipart);
        }

        Assert.AreEqual(expected.HasLongNames, actual.HasLongNames);

        if (expected.Tiles is not null)
        {
            Assert.IsNotNull(actual.Tiles);
            Assert.AreEqual(expected.Tiles!.TileSizeX, actual.Tiles!.TileSizeX);
            Assert.AreEqual(expected.Tiles.TileSizeY, actual.Tiles.TileSizeY);
            Assert.AreEqual(expected.Tiles.LevelMode, actual.Tiles.LevelMode);
            Assert.AreEqual(expected.Tiles.RoundingMode, actual.Tiles.RoundingMode);
        }

        for (int i = 0; i < expected.Channels.Count; i++)
        {
            ExrChannel expectedChannel = expected.Channels[i];
            ExrChannel actualChannel = actual.Channels[i];
            Assert.AreEqual(expectedChannel.Name, actualChannel.Name);
            Assert.AreEqual(expectedChannel.Type, actualChannel.Type);
            Assert.AreEqual(expectedChannel.SamplingX, actualChannel.SamplingX);
            Assert.AreEqual(expectedChannel.SamplingY, actualChannel.SamplingY);
            Assert.AreEqual(expectedChannel.Linear, actualChannel.Linear);
            Assert.AreEqual(expectedChannel.RequestedPixelType, actualChannel.RequestedPixelType);
        }

        Assert.AreEqual(expected.CustomAttributes.Count, actual.CustomAttributes.Count);
        ExrAttribute[] expectedAttributes = expected.CustomAttributes
            .OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .ThenBy(static attribute => attribute.TypeName, StringComparer.Ordinal)
            .ToArray();
        ExrAttribute[] actualAttributes = actual.CustomAttributes
            .OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .ThenBy(static attribute => attribute.TypeName, StringComparer.Ordinal)
            .ToArray();
        for (int i = 0; i < expectedAttributes.Length; i++)
        {
            ExrAttribute expectedAttribute = expectedAttributes[i];
            ExrAttribute actualAttribute = actualAttributes[i];
            Assert.AreEqual(expectedAttribute.Name, actualAttribute.Name);
            Assert.AreEqual(expectedAttribute.TypeName, actualAttribute.TypeName, expectedAttribute.Name);
            CollectionAssert.AreEqual(expectedAttribute.Value, actualAttribute.Value, expectedAttribute.Name);
        }
    }

    public static void EqualImages(ExrImage expected, ExrImage actual)
    {
        CompressionType? compression = ImageComparisonSettingsByImage.TryGetValue(expected, out ImageComparisonSettings? settings)
            ? settings.Compression
            : null;

        Assert.AreEqual(expected.Width, actual.Width);
        Assert.AreEqual(expected.Height, actual.Height);
        Assert.AreEqual(expected.Channels.Count, actual.Channels.Count);
        Assert.AreEqual(expected.Levels.Count, actual.Levels.Count);

        for (int levelIndex = 0; levelIndex < expected.Levels.Count; levelIndex++)
        {
            ExrImageLevel expectedLevel = expected.Levels[levelIndex];
            ExrImageLevel actualLevel = actual.Levels[levelIndex];
            Assert.AreEqual(expectedLevel.LevelX, actualLevel.LevelX);
            Assert.AreEqual(expectedLevel.LevelY, actualLevel.LevelY);
            Assert.AreEqual(expectedLevel.Width, actualLevel.Width);
            Assert.AreEqual(expectedLevel.Height, actualLevel.Height);
            Assert.AreEqual(expectedLevel.Channels.Count, actualLevel.Channels.Count);
            Assert.AreEqual(expectedLevel.Tiles.Count, actualLevel.Tiles.Count);

            for (int channelIndex = 0; channelIndex < expectedLevel.Channels.Count; channelIndex++)
            {
                ExrImageChannel expectedChannel = expectedLevel.Channels[channelIndex];
                ExrImageChannel actualChannel = actualLevel.Channels[channelIndex];
                EqualImageChannels(
                    expectedChannel,
                    actualChannel,
                    compression,
                    $"level ({expectedLevel.LevelX}, {expectedLevel.LevelY}) channel {channelIndex}");
            }

            for (int tileIndex = 0; tileIndex < expectedLevel.Tiles.Count; tileIndex++)
            {
                ExrTile expectedTile = expectedLevel.Tiles[tileIndex];
                ExrTile actualTile = actualLevel.Tiles[tileIndex];
                Assert.AreEqual(expectedTile.OffsetX, actualTile.OffsetX);
                Assert.AreEqual(expectedTile.OffsetY, actualTile.OffsetY);
                Assert.AreEqual(expectedTile.LevelX, actualTile.LevelX);
                Assert.AreEqual(expectedTile.LevelY, actualTile.LevelY);
                Assert.AreEqual(expectedTile.Width, actualTile.Width);
                Assert.AreEqual(expectedTile.Height, actualTile.Height);
                Assert.AreEqual(expectedTile.Channels.Count, actualTile.Channels.Count);

                for (int channelIndex = 0; channelIndex < expectedTile.Channels.Count; channelIndex++)
                {
                    EqualImageChannels(
                        expectedTile.Channels[channelIndex],
                        actualTile.Channels[channelIndex],
                        compression,
                        $"level ({expectedLevel.LevelX}, {expectedLevel.LevelY}) tile {tileIndex} channel {channelIndex}");
                }
            }
        }
    }

    private static void TrackCompression(ExrImage image, CompressionType compression)
    {
        ImageComparisonSettingsByImage.Add(image, new ImageComparisonSettings(compression));
    }

    private static void TrackHeader(ExrHeader header)
    {
        LineOrderType encodedLineOrder = header.Tiles is null
            ? LineOrderType.IncreasingY
            : header.LineOrder;
        HeaderComparisonSettingsByHeader.Add(header, new HeaderComparisonSettings(encodedLineOrder));
    }

    private static void EqualBoxes(ExrBox2i expected, ExrBox2i actual, string name)
    {
        Assert.AreEqual(expected.MinX, actual.MinX, $"{name}.MinX");
        Assert.AreEqual(expected.MinY, actual.MinY, $"{name}.MinY");
        Assert.AreEqual(expected.MaxX, actual.MaxX, $"{name}.MaxX");
        Assert.AreEqual(expected.MaxY, actual.MaxY, $"{name}.MaxY");
    }

    private static void EqualImageChannels(
        ExrImageChannel expected,
        ExrImageChannel actual,
        CompressionType? compression,
        string context)
    {
        Assert.AreEqual(expected.Channel.Name, actual.Channel.Name, context);
        Assert.AreEqual(expected.Channel.Type, actual.Channel.Type, context);
        Assert.AreEqual(expected.Channel.RequestedPixelType, actual.Channel.RequestedPixelType, context);
        Assert.AreEqual(expected.Channel.SamplingX, actual.Channel.SamplingX, context);
        Assert.AreEqual(expected.Channel.SamplingY, actual.Channel.SamplingY, context);
        Assert.AreEqual(expected.Channel.Linear, actual.Channel.Linear, context);
        Assert.AreEqual(expected.DataType, actual.DataType, context);
        Assert.AreEqual(expected.Data.Length, actual.Data.Length, context);

        if (expected.Data.AsSpan().SequenceEqual(actual.Data))
        {
            return;
        }

        float? tolerance = GetLossyTolerance(compression, expected.Channel.Type);
        if (tolerance is null)
        {
            CollectionAssert.AreEqual(expected.Data, actual.Data, context);
            return;
        }

        AssertLossyChannelData(expected, actual, tolerance.Value, context);
    }

    private static float? GetLossyTolerance(CompressionType? compression, ExrPixelType storedType)
    {
        if (compression == CompressionType.PXR24 && storedType == ExrPixelType.Float)
        {
            return Pxr24Tolerance;
        }

        if ((compression == CompressionType.B44 || compression == CompressionType.B44A) &&
            storedType == ExrPixelType.Half)
        {
            return B44Tolerance;
        }

        return null;
    }

    private static void AssertLossyChannelData(
        ExrImageChannel expected,
        ExrImageChannel actual,
        float tolerance,
        string context)
    {
        switch (expected.DataType)
        {
            case ExrPixelType.Half:
                ReadOnlySpan<ushort> expectedHalf = MemoryMarshal.Cast<byte, ushort>(expected.Data);
                ReadOnlySpan<ushort> actualHalf = MemoryMarshal.Cast<byte, ushort>(actual.Data);
                for (int i = 0; i < expectedHalf.Length; i++)
                {
                    AssertLossySampleEqual(
                        (float)BitConverter.UInt16BitsToHalf(expectedHalf[i]),
                        (float)BitConverter.UInt16BitsToHalf(actualHalf[i]),
                        tolerance,
                        context,
                        i);
                }

                break;
            case ExrPixelType.Float:
                ReadOnlySpan<float> expectedFloat = MemoryMarshal.Cast<byte, float>(expected.Data);
                ReadOnlySpan<float> actualFloat = MemoryMarshal.Cast<byte, float>(actual.Data);
                for (int i = 0; i < expectedFloat.Length; i++)
                {
                    AssertLossySampleEqual(expectedFloat[i], actualFloat[i], tolerance, context, i);
                }

                break;
            case ExrPixelType.UInt:
                CollectionAssert.AreEqual(expected.Data, actual.Data, context);
                break;
            default:
                Assert.Fail($"{context}: unsupported decoded pixel type {expected.DataType}.");
                break;
        }
    }

    private static void AssertLossySampleEqual(
        float expected,
        float actual,
        float tolerance,
        string context,
        int sampleIndex)
    {
        if (expected.Equals(actual) || (float.IsNaN(expected) && float.IsNaN(actual)))
        {
            return;
        }

        float error = MathF.Abs(expected - actual);
        Assert.IsTrue(
            float.IsFinite(error) && error <= tolerance,
            $"{context}: sample {sampleIndex} expected {expected}, actual {actual}, error {error}, tolerance {tolerance}.");
    }

    private sealed class ImageComparisonSettings
    {
        public ImageComparisonSettings(CompressionType compression)
        {
            Compression = compression;
        }

        public CompressionType Compression { get; }
    }

    private sealed class HeaderComparisonSettings
    {
        public HeaderComparisonSettings(LineOrderType encodedLineOrder)
        {
            EncodedLineOrder = encodedLineOrder;
        }

        public LineOrderType EncodedLineOrder { get; }
    }

    public static ExrImageChannel FloatChannel(string name, ExrPixelType storedType, float[] samples)
    {
        return new ExrImageChannel(new ExrChannel(name, storedType), ExrPixelType.Float, ToBytes(samples));
    }

    public static ExrImageChannel UIntChannel(string name, ExrPixelType storedType, uint[] samples)
    {
        return new ExrImageChannel(new ExrChannel(name, storedType), ExrPixelType.UInt, ToBytes(samples));
    }

    public static byte[] ToBytes(float[] values)
    {
        byte[] bytes = new byte[checked(values.Length * sizeof(float))];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static byte[] ToBytes(uint[] values)
    {
        byte[] bytes = new byte[checked(values.Length * sizeof(uint))];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] ReadFloatChannel(ExrImage image, string channelName)
    {
        return ReadFloatChannel(image.GetChannel(channelName));
    }

    public static float[] ReadFloatChannel(ExrImageChannel channel)
    {
        Assert.AreEqual(ExrPixelType.Float, channel.DataType);
        return MemoryMarshal.Cast<byte, float>(channel.Data).ToArray();
    }

    public static uint[] ReadUIntChannel(ExrImage image, string channelName)
    {
        ExrImageChannel channel = image.GetChannel(channelName);
        Assert.AreEqual(ExrPixelType.UInt, channel.DataType);
        return MemoryMarshal.Cast<byte, uint>(channel.Data).ToArray();
    }

    public static void SetRequestedPixelTypes(ExrHeader header, Func<ExrChannel, ExrPixelType> selector)
    {
        foreach (ExrChannel channel in header.Channels)
        {
            channel.RequestedPixelType = selector(channel);
        }
    }

    public static void AssertMaxError(float[] expected, float[] actual, float tolerance, string channelName)
    {
        Assert.AreEqual(expected.Length, actual.Length);

        float maxError = 0.0f;
        int maxErrorIndex = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            float error = MathF.Abs(expected[i] - actual[i]);
            if (error > maxError)
            {
                maxError = error;
                maxErrorIndex = i;
            }
        }

        Assert.IsTrue(
            maxError <= tolerance,
            $"Channel '{channelName}' exceeded tolerance {tolerance} with max error {maxError} at sample {maxErrorIndex}.");
    }
}

internal static class ExrBinaryMutationHelper
{
    public static void SetHeaderByteAttributeValue(byte[] encoded, int headerIndex, string attributeName, string attributeType, byte value)
    {
        int valueOffset = FindHeaderAttributeValueOffset(encoded, headerIndex, attributeName, attributeType, out int valueSize);
        Assert.AreEqual(1, valueSize, $"{attributeName} attribute must be 1 byte.");
        encoded[valueOffset] = value;
    }

    public static void ReplaceHeaderCStringAttributeValue(byte[] encoded, int headerIndex, string attributeName, string attributeType, string value)
    {
        int valueOffset = FindHeaderAttributeValueOffset(encoded, headerIndex, attributeName, attributeType, out int valueSize);
        byte[] replacement = Encoding.UTF8.GetBytes(value + "\0");
        Assert.AreEqual(valueSize, replacement.Length, $"{attributeName} replacement must preserve the encoded length.");
        Buffer.BlockCopy(replacement, 0, encoded, valueOffset, replacement.Length);
    }

    public static byte[] TruncateFirstScanlineChunkPayload(byte[] encoded, int bytesRemovedFromPayload)
    {
        long chunkOffset = ReadFirstSinglePartChunkOffset(encoded);
        int packedSizeOffset = checked((int)chunkOffset + sizeof(int));
        Assert.IsTrue(packedSizeOffset + sizeof(int) <= encoded.Length, "scanline chunk payload size field was truncated.");

        int packedSize = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(packedSizeOffset, sizeof(int)));
        int payloadOffset = packedSizeOffset + sizeof(int);
        Assert.IsTrue(packedSize > 0, "scanline chunk payload size must be positive.");
        Assert.IsTrue(payloadOffset + packedSize == encoded.Length, "expected the first scanline chunk payload to extend to EOF.");
        Assert.IsTrue(
            bytesRemovedFromPayload > 0 && bytesRemovedFromPayload <= packedSize,
            $"bytesRemovedFromPayload must be in [1, {packedSize}].");

        byte[] mutated = new byte[encoded.Length - bytesRemovedFromPayload];
        Buffer.BlockCopy(encoded, 0, mutated, 0, mutated.Length);
        return mutated;
    }

    public static int FindHeaderAttributeValueOffset(byte[] encoded, int headerIndex, string attributeName, string attributeType, out int valueSize)
    {
        int currentHeaderIndex = 0;
        int offset = 8;
        while (offset < encoded.Length)
        {
            int nameEnd = Array.IndexOf(encoded, (byte)0, offset);
            Assert.IsTrue(nameEnd >= 0, "attribute name terminator was not found.");
            if (nameEnd == offset)
            {
                if (currentHeaderIndex == headerIndex)
                {
                    break;
                }

                currentHeaderIndex++;
                offset++;
                continue;
            }

            string name = Encoding.UTF8.GetString(encoded, offset, nameEnd - offset);
            offset = nameEnd + 1;

            int typeEnd = Array.IndexOf(encoded, (byte)0, offset);
            Assert.IsTrue(typeEnd >= 0, "attribute type terminator was not found.");
            string type = Encoding.UTF8.GetString(encoded, offset, typeEnd - offset);
            offset = typeEnd + 1;

            Assert.IsTrue(offset + sizeof(int) <= encoded.Length, "attribute size field was truncated.");
            valueSize = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(offset, sizeof(int)));
            Assert.IsTrue(valueSize >= 0, "attribute size must be non-negative.");
            offset += sizeof(int);

            Assert.IsTrue(offset + valueSize <= encoded.Length, "attribute value was truncated.");
            if (currentHeaderIndex == headerIndex &&
                string.Equals(name, attributeName, StringComparison.Ordinal) &&
                string.Equals(type, attributeType, StringComparison.Ordinal))
            {
                return offset;
            }

            offset += valueSize;
        }

        throw new AssertFailedException($"Attribute '{attributeName}' of type '{attributeType}' was not found in header {headerIndex}.");
    }

    private static long ReadFirstSinglePartChunkOffset(byte[] encoded)
    {
        int offsetTableOffset = FindSinglePartOffsetTableOffset(encoded);
        Assert.IsTrue(offsetTableOffset + sizeof(long) <= encoded.Length, "offset table entry was truncated.");

        long chunkOffset = BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(offsetTableOffset, sizeof(long)));
        Assert.AreEqual(offsetTableOffset + sizeof(long), chunkOffset, "expected a single scanline chunk immediately after the offset table.");
        return chunkOffset;
    }

    private static int FindSinglePartOffsetTableOffset(byte[] encoded)
    {
        int offset = 8;
        while (true)
        {
            int nameEnd = Array.IndexOf(encoded, (byte)0, offset);
            Assert.IsTrue(nameEnd >= 0, "attribute name terminator was not found.");
            if (nameEnd == offset)
            {
                return offset + 1;
            }

            offset = nameEnd + 1;

            int typeEnd = Array.IndexOf(encoded, (byte)0, offset);
            Assert.IsTrue(typeEnd >= 0, "attribute type terminator was not found.");
            offset = typeEnd + 1;

            Assert.IsTrue(offset + sizeof(int) <= encoded.Length, "attribute size field was truncated.");
            int attributeSize = BinaryPrimitives.ReadInt32LittleEndian(encoded.AsSpan(offset, sizeof(int)));
            Assert.IsTrue(attributeSize >= 0, "attribute size must be non-negative.");
            offset += sizeof(int);

            Assert.IsTrue(offset + attributeSize <= encoded.Length, "attribute value was truncated.");
            offset += attributeSize;
        }
    }
}
