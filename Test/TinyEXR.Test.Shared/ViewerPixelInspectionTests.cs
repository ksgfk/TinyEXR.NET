using System.Buffers.Binary;
using System.Globalization;
using TinyEXR.Viewer.Models;
using TinyEXR.Viewer.Services;
using V3 = TinyEXR;

namespace TinyEXR.Test;

[TestClass]
public sealed class ViewerPixelInspectionTests
{
    [TestMethod(DisplayName = "Viewer pixel picking maps centered wide and tall previews at different scales")]
    [DataRow(800.0, 600.0, 400, 200, 1.0, 101.0, 0, 0)]
    [DataRow(800.0, 600.0, 400, 200, 799.9, 499.9, 399, 199)]
    [DataRow(800.0, 600.0, 200, 400, 250.1, 0.1, 0, 0)]
    [DataRow(800.0, 600.0, 200, 400, 549.9, 599.9, 199, 399)]
    [DataRow(80.0, 60.0, 400, 200, 79.99, 49.99, 399, 199)]
    [DataRow(801.0, 601.0, 400, 200, 401.0, 301.0, 200, 100)]
    public void PickingMapsUniformPreview(double viewportWidth, double viewportHeight, int width, int height,
        double pointerX, double pointerY, int expectedX, int expectedY)
    {
        Assert.IsTrue(PreviewPixelMapper.TryGetPixel(viewportWidth, viewportHeight, width, height,
            pointerX, pointerY, out int x, out int y));
        Assert.AreEqual(expectedX, x);
        Assert.AreEqual(expectedY, y);
    }

    [TestMethod(DisplayName = "Viewer pixel picking ignores letterboxing and exclusive image edges")]
    [DataRow(0.0, 99.9)]
    [DataRow(400.0, 500.0)]
    [DataRow(800.0, 300.0)]
    [DataRow(-0.1, 300.0)]
    [DataRow(400.0, -1.0)]
    public void PickingIgnoresMarginsAndEdges(double pointerX, double pointerY)
    {
        Assert.IsFalse(PreviewPixelMapper.TryGetPixel(800, 600, 400, 200,
            pointerX, pointerY, out _, out _));
    }

    [TestMethod(DisplayName = "Viewer pixel picking rejects empty layouts and non-finite input")]
    public void PickingRejectsInvalidLayout()
    {
        Assert.IsFalse(PreviewPixelMapper.TryGetPixel(0, 600, 400, 200, 0, 0, out _, out _));
        Assert.IsFalse(PreviewPixelMapper.TryGetPixel(800, 600, 0, 200, 0, 0, out _, out _));
        Assert.IsFalse(PreviewPixelMapper.TryGetPixel(double.PositiveInfinity, 600, 400, 200, 0, 0, out _, out _));
        Assert.IsFalse(PreviewPixelMapper.TryGetPixel(800, 600, 400, 200, double.NaN, 300, out _, out _));
    }

    [TestMethod(DisplayName = "Viewer pixel inspection retains HDR Half, Float and exact UInt values at offset coordinates")]
    public void InspectionPreservesOriginalValues()
    {
        V3.Box2i region = new(-2, 7, -1, 8);
        ExrPartDocument document = CreateDocument(region,
            [new("R", V3.PixelType.Half), new("G", V3.PixelType.Float), new("ID", V3.PixelType.UInt)],
            [Buffer("R", V3.PixelType.Half, 0, 1, 2, -2.5),
             Buffer("G", V3.PixelType.Float, 0, 1, 2, 12.75),
             Buffer("ID", V3.PixelType.UInt, 0, 1, 2, uint.MaxValue)]);

        PixelInspection inspection = PixelInspectionReader.Inspect(document, 0, null, 1, 1)!;
        Assert.IsNotNull(inspection);
        Assert.AreEqual(-1, inspection.ExrX);
        Assert.AreEqual(8, inspection.ExrY);
        Assert.AreEqual("-2.5", inspection.Channels.Single(channel => channel.Name == "R").Value);
        Assert.AreEqual("12.75", inspection.Channels.Single(channel => channel.Name == "G").Value);
        Assert.AreEqual("4294967295", inspection.Channels.Single(channel => channel.Name == "ID").Value);
    }

    [TestMethod(DisplayName = "Viewer pixel inspection reads only the selected layer and level")]
    public void InspectionUsesSelectedLayerAndLevel()
    {
        V3.Box2i region = new(10, -4, 11, -3);
        V3.Channel[] channels = [new("R", V3.PixelType.Float), new("beauty.R", V3.PixelType.Float)];
        V3.Header header = new(V3.PartType.Tiled, region, channels,
            tiles: new V3.TileDescription(2, 2, V3.TileLevelMode.MipmapLevels));
        V3.FlatLevel level0 = new(0, 0, region,
            [Buffer("R", V3.PixelType.Float, 1, 2, 3, 4), Buffer("beauty.R", V3.PixelType.Float, 5, 6, 7, 8)]);
        V3.FlatLevel level1 = new(1, 1, new V3.Box2i(10, -4, 10, -4),
            [Buffer("R", V3.PixelType.Float, 9), Buffer("beauty.R", V3.PixelType.Float, 10)]);
        ExrPartDocument document = new()
        {
            Index = 3,
            Header = header,
            Part = new V3.Part(header, [level0, level1], isComplete: true),
            HasRootLayer = true,
        };

        PixelInspection root = PixelInspectionReader.Inspect(document, 0, null, 0, 0)!;
        Assert.AreEqual(1, root.Channels.Count);
        Assert.AreEqual("R", root.Channels[0].Name);
        Assert.AreEqual("1", root.Channels[0].Value);
        PixelInspection beauty = PixelInspectionReader.Inspect(document, 1, "beauty", 0, 0)!;
        Assert.AreEqual(1, beauty.Channels.Count);
        Assert.AreEqual("beauty.R", beauty.Channels[0].Name);
        Assert.AreEqual("10", beauty.Channels[0].Value);
        Assert.AreEqual(10, beauty.ExrX);
        Assert.AreEqual(-4, beauty.ExrY);
        StringAssert.Contains(beauty.Context, "L(1, 1)");
        Assert.IsNull(PixelInspectionReader.Inspect(document, 1, "beauty", 1, 0));
        Assert.IsNull(PixelInspectionReader.Inspect(document, -1, null, 0, 0));
        Assert.IsNull(PixelInspectionReader.Inspect(document, 0, null, -1, 0));
    }

    [TestMethod(DisplayName = "Viewer pixel inspection reports held subsamples at negative EXR coordinates")]
    [DataRow(0, 0, "1", "Sample at EXR (-2, -3)")]
    [DataRow(1, 1, "1", "")]
    [DataRow(2, 2, "1", "Sample at EXR (-2, -3)")]
    [DataRow(3, 4, "5", "")]
    [DataRow(5, 7, "9", "")]
    public void InspectionReadsSubsampledChannels(int x, int y, string expectedValue, string expectedLocation)
    {
        V3.Box2i region = new(-3, -4, 2, 3);
        ExrPartDocument document = CreateDocument(region,
            [new("S", V3.PixelType.Float, xSampling: 2, ySampling: 3)],
            [Buffer("S", V3.PixelType.Float, 1, 2, 3, 4, 5, 6, 7, 8, 9)]);

        PixelChannelValue value = PixelInspectionReader.Inspect(document, 0, null, x, y)!.Channels[0];
        Assert.AreEqual(expectedValue, value.Value);
        Assert.AreEqual(expectedLocation, value.SampleLocation);
    }

    [TestMethod(DisplayName = "Viewer pixel inspection reports channels with no stored sample")]
    public void InspectionHandlesEmptySamples()
    {
        ExrPartDocument document = CreateDocument(new V3.Box2i(1, 1, 1, 1),
            [new("S", V3.PixelType.Half, xSampling: 2, ySampling: 3)],
            [new V3.ChannelBuffer("S", V3.PixelType.Half, ReadOnlySpan<byte>.Empty)]);
        Assert.AreEqual("No sample", PixelInspectionReader.Inspect(document, 0, null, 0, 0)!.Channels[0].Value);
    }

    [TestMethod(DisplayName = "Viewer pixel inspection preserves NaN and Infinity with culture-independent precision")]
    public void InspectionPreservesSpecialValues()
    {
        ExrPartDocument document = CreateDocument(new V3.Box2i(0, 0, 3, 0),
            [new("R", V3.PixelType.Float)],
            [Buffer("R", V3.PixelType.Float, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.123456789f)]);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.AreEqual("NaN", PixelInspectionReader.Inspect(document, 0, null, 0, 0)!.Channels[0].Value);
            Assert.AreEqual("Infinity", PixelInspectionReader.Inspect(document, 0, null, 1, 0)!.Channels[0].Value);
            Assert.AreEqual("-Infinity", PixelInspectionReader.Inspect(document, 0, null, 2, 0)!.Channels[0].Value);
            string value = PixelInspectionReader.Inspect(document, 0, null, 3, 0)!.Channels[0].Value;
            Assert.AreEqual(0.123456789f, float.Parse(value, CultureInfo.InvariantCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static ExrPartDocument CreateDocument(V3.Box2i region, V3.Channel[] channels, V3.ChannelBuffer[] buffers)
    {
        V3.Header header = new(V3.PartType.Scanline, region, channels);
        return new ExrPartDocument
        {
            Index = 0,
            Header = header,
            Part = new V3.Part(header, [new V3.FlatLevel(0, 0, region, buffers)], isComplete: true),
            HasRootLayer = true,
        };
    }

    private static V3.ChannelBuffer Buffer(string name, V3.PixelType type, params double[] values)
    {
        int typeSize = type == V3.PixelType.Half ? 2 : 4;
        byte[] bytes = new byte[values.Length * typeSize];
        for (int i = 0; i < values.Length; i++)
        {
            Span<byte> sample = bytes.AsSpan(i * typeSize, typeSize);
            switch (type)
            {
                case V3.PixelType.Half:
                    BinaryPrimitives.WriteUInt16LittleEndian(sample, BitConverter.HalfToUInt16Bits((Half)values[i]));
                    break;
                case V3.PixelType.Float:
                    BinaryPrimitives.WriteInt32LittleEndian(sample, BitConverter.SingleToInt32Bits((float)values[i]));
                    break;
                case V3.PixelType.UInt:
                    BinaryPrimitives.WriteUInt32LittleEndian(sample, checked((uint)values[i]));
                    break;
            }
        }

        return new V3.ChannelBuffer(name, type, bytes);
    }
}
