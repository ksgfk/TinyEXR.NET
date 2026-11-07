using TinyEXR;
using TinyEXR.IO;

namespace TinyEXR.NamespaceMigration.Tests;

[TestClass]
public sealed class NamespaceMigrationTests
{
    [TestMethod]
    public void ModernPublicTypesUseUnversionedNamespaces()
    {
        string[] expected =
        {
            "TinyEXR.ExrResult",
            "TinyEXR.PixelType",
            "TinyEXR.Compression",
            "TinyEXR.LineOrder",
            "TinyEXR.PartType",
            "TinyEXR.TileLevelMode",
            "TinyEXR.TileRoundingMode",
            "TinyEXR.ResizeFilter",
            "TinyEXR.EdgeMode",
            "TinyEXR.ToneMapOperator",
            "TinyEXR.ColorSpace",
            "TinyEXR.TransferFunction",
            "TinyEXR.LutInterpolation",
            "TinyEXR.SpectralType",
            "TinyEXR.PixelConversionMode",
            "TinyEXR.ExrFile",
            "TinyEXR.Box2i",
            "TinyEXR.Chromaticities",
            "TinyEXR.IO.DataTransferStatus",
            "TinyEXR.IO.DataRange",
            "TinyEXR.IO.DataTransferResult",
            "TinyEXR.IO.IDataSourceLength",
            "TinyEXR.IO.IExactDataSource",
            "TinyEXR.IO.IAsyncExactDataSource",
            "TinyEXR.IO.ISeekableDataSink",
            "TinyEXR.IO.IAsyncSeekableDataSink",
            "TinyEXR.IO.MemoryDataSource",
            "TinyEXR.IO.StreamDataSink",
            "TinyEXR.IO.StreamDataSource",
            "TinyEXR.IO.SuppliedDataSource",
            "TinyEXR.ChannelBuffer",
            "TinyEXR.PartLevel",
            "TinyEXR.FlatLevel",
            "TinyEXR.DeepSampleRange",
            "TinyEXR.DeepLevel",
            "TinyEXR.Part",
            "TinyEXR.Image",
            "TinyEXR.ColorMatrix3x3",
            "TinyEXR.ToneMapParameters",
            "TinyEXR.ImageProcessing",
            "TinyEXR.Lut3D",
            "TinyEXR.Channel",
            "TinyEXR.HeaderAttribute",
            "TinyEXR.TileDescription",
            "TinyEXR.Header",
            "TinyEXR.InterleavedFloatImage",
            "TinyEXR.PartConversion",
            "TinyEXR.ExrReader",
            "TinyEXR.ExrContext",
            "TinyEXR.PreviewImage",
            "TinyEXR.ReaderState",
            "TinyEXR.ReaderLimits",
            "TinyEXR.ReaderOptions",
            "TinyEXR.ReaderLimitExceededException",
            "TinyEXR.ReaderResult",
            "TinyEXR.ReaderResult`1",
            "TinyEXR.BlockInfo",
            "TinyEXR.DeepChannelDestination",
            "TinyEXR.PixelConversion",
            "TinyEXR.SimdCapabilities",
            "TinyEXR.SimdRuntime",
            "TinyEXR.Spectral",
            "TinyEXR.SpectralImage",
            "TinyEXR.StreamingImageResizer",
            "TinyEXR.ExrWriter",
            "TinyEXR.WriterState",
            "TinyEXR.WriterLimits",
            "TinyEXR.WriterOptions",
            "TinyEXR.WriterLimitExceededException",
            "TinyEXR.WriterResult",
            "TinyEXR.WriterResult`1",
        };
        Type[] exported = typeof(ExrFile).Assembly.GetExportedTypes();
        Assert.AreEqual(71, expected.Length);
        foreach (string name in expected)
        {
            Assert.IsTrue(exported.Any(type => type.FullName == name), name);
        }
        Assert.IsFalse(exported.Any(type => type.Namespace?.StartsWith("TinyEXR.V3", StringComparison.Ordinal) == true));
        Assert.AreEqual(new Version(1, 2, 0, 0), typeof(ExrFile).Assembly.GetName().Version);
        Assert.AreEqual(3, typeof(ExrFile).GetField(nameof(ExrFile.ApiVersionMajor))!.GetRawConstantValue());
        Assert.AreEqual(1, typeof(ExrFile).GetField(nameof(ExrFile.ApiVersionMinor))!.GetRawConstantValue());
    }

    [TestMethod]
    public void RootNamespaceAndIoImportsCompileAndRoundTrip()
    {
        Part part = Spectral.CreateEmissivePart(1, 1, new[] { 550.0f }, new[] { 2.5f }, "radiance");
        Image image = new(new[] { part });
        WriterResult<byte[]> saved = ExrFile.SaveToMemory(image, Compression.ZIP);
        Assert.IsTrue(saved.IsSuccess, saved.Error?.ToString());
        Assert.IsNotNull(saved.Value);
        ReaderResult<Image> loaded = ExrFile.LoadFromMemory(saved.Value);
        Assert.IsTrue(loaded.IsSuccess, loaded.Error?.ToString());
        Assert.IsNotNull(loaded.Value);
        Assert.AreEqual(1, loaded.Value.Parts.Count);
        IExactDataSource source = new MemoryDataSource(saved.Value);
        using ExrReader reader = ExrReader.OpenSource(source);
        Assert.IsTrue(reader.ParseHeader().IsSuccess);
    }

    [TestMethod]
    public void RemovedTypesAndImplementationsAreAbsent()
    {
        Assert.IsTrue(File.ReadAllText(Path.Combine(TinyEXR.Test.TestPaths.RepoRoot, "TinyEXR.NET", "TinyEXR.NET.csproj"))
            .Contains("<AllowUnsafeBlocks>false</AllowUnsafeBlocks>", StringComparison.Ordinal));
        string[] removed = { "ExrVersion", "ExrHeader", "ExrImage", "ExrDeepImage", "ExrMultipartHeader",
            "ExrMultipartImage", "ExrChannel", "ExrAttribute", "ResultCode", "ExrPixelType", "CompressionType",
            "LineOrderType", "SpectrumType", "ExrBox2i", "ExrTile", "ExrImageLevel", "ExrImageChannel",
            "ExrDeepChannel", "ExrTileDescription", "ExrTileLevelMode", "ExrTileRoundingMode",
            "SinglePartExrReader", "ScanlineExrWriter", "V1FacadeAdapter", "ExrImplementation" };
        Type[] types = typeof(Exr).Assembly.GetTypes();
        Assert.IsFalse(types.Any(t => t.Namespace?.Contains("PortV1", StringComparison.Ordinal) == true));
        foreach (string name in removed)
            Assert.IsFalse(types.Any(t => t.Name == name), name);
        Header rgb = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("R", PixelType.Float) });
        Assert.AreEqual(SpectralType.None, Exr.EXRGetSpectrumType(rgb));
        Assert.AreEqual(typeof(SpectralType), typeof(Exr).GetMethod(nameof(Exr.EXRGetSpectrumType))!.ReturnType);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(TinyEXR.Test.TestPaths.RepoRoot, "TinyEXR.NET"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                continue;
            string source = File.ReadAllText(path);
            Assert.IsFalse(source.Contains("PortV1", StringComparison.Ordinal), path);
            Assert.IsFalse(source.Contains("ResultCode", StringComparison.Ordinal), path);
        }
    }
}
