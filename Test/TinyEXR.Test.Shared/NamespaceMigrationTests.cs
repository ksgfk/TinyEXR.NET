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
        Assert.AreEqual(69, expected.Length);
        foreach (string name in expected)
        {
            Assert.IsTrue(exported.Any(type => type.FullName == name), name);
        }
        Assert.IsFalse(exported.Any(type => type.Namespace?.StartsWith("TinyEXR.V3", StringComparison.Ordinal) == true));
        Assert.AreEqual(new Version(1, 2, 0, 0), typeof(ExrFile).Assembly.GetName().Version);
        Assert.AreEqual(3, typeof(ExrFile).GetField(nameof(ExrFile.ApiVersionMajor))!.GetRawConstantValue());
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
    public void LegacyAndModernSpectralEnumsKeepDistinctValuesAndAbsenceSemantics()
    {
        Assert.AreEqual(0, Convert.ToInt32(Enum.Parse(typeof(SpectrumType), nameof(SpectrumType.Reflective))));
        Assert.AreEqual(1, Convert.ToInt32(Enum.Parse(typeof(SpectrumType), nameof(SpectrumType.Emissive))));
        Assert.AreEqual(2, Convert.ToInt32(Enum.Parse(typeof(SpectrumType), nameof(SpectrumType.Polarised))));
        Assert.AreEqual(0, Convert.ToInt32(Enum.Parse(typeof(SpectralType), nameof(SpectralType.None))));
        Assert.AreEqual(1, Convert.ToInt32(Enum.Parse(typeof(SpectralType), nameof(SpectralType.Reflective))));
        Assert.AreEqual(2, Convert.ToInt32(Enum.Parse(typeof(SpectralType), nameof(SpectralType.Emissive))));
        Assert.AreEqual(3, Convert.ToInt32(Enum.Parse(typeof(SpectralType), nameof(SpectralType.Polarised))));
        Assert.IsNull(Exr.EXRGetSpectrumType(new ExrHeader()));
        Header rgb = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("R", PixelType.Float) });
        Assert.AreEqual(SpectralType.None, Spectral.GetSpectrumType(rgb));
        Assert.AreEqual(typeof(SpectralType), typeof(SpectralImage).GetProperty(nameof(SpectralImage.SpectrumType))!.PropertyType);
        Assert.AreEqual(typeof(SpectralType), typeof(Spectral).GetMethod(nameof(Spectral.GetSpectrumType))!.ReturnType);
        ExrHeader legacy = new();
        legacy.Channels.Add(new ExrChannel("S0.550,000000nm", ExrPixelType.Float));
        Assert.AreEqual(ResultCode.Success, Exr.EXRSetSpectralAttributes(legacy, SpectrumType.Emissive, "radiance"));
        Assert.AreEqual(SpectrumType.Emissive, Exr.EXRGetSpectrumType(legacy));
    }
}
