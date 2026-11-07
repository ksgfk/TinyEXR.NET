using System.Buffers.Binary;
using System.Text;

namespace TinyEXR.Test;

[TestClass]
public sealed class StandardAttributeTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] StandardAttributes|OfficialSamples|RawRoundTrip")]
    public void Case_StandardAttributes_OfficialSamples_raw_round_trip()
    {
        foreach (ExpectedAttributeSample sample in new[]
        {
            new ExpectedAttributeSample("MultiView/Impact.exr", new[] { "multiView", "wrapmodes" }),
            new ExpectedAttributeSample("MultiResolution/Kapaa.exr", new[] { "preview", "wrapmodes" }),
            new ExpectedAttributeSample("MultiResolution/StageEnvCube.exr", new[] { "envmap", "preview" }),
        })
        {
            string path = TestPaths.OpenExr(sample.RelativePath);
            (uint _, Header header, Part image) = ExrTestHelper.LoadSinglePart(path);
            Dictionary<string, HeaderAttribute> expected = sample.AttributeNames.ToDictionary(
                static name => name,
                name => CloneAttribute(GetCustomAttribute(header, name)),
                StringComparer.Ordinal);

            Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(image, out byte[] encoded), sample.RelativePath);
            Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out Header? roundTrippedHeader), sample.RelativePath);
            Assert.IsNotNull(roundTrippedHeader);

            foreach ((string attributeName, HeaderAttribute expectedAttribute) in expected)
            {
                HeaderAttribute actualAttribute = GetCustomAttribute(roundTrippedHeader!, attributeName);
                AssertAttributeEqual(expectedAttribute, actualAttribute, $"{sample.RelativePath}|{attributeName}");
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] StandardAttributes|SyntheticRawRoundTrip")]
    public void Case_StandardAttributes_Synthetic_raw_round_trip()
    {
        Header header = new(PartType.Scanline, new Box2i(0, 0, 0, 0), new[] { new Channel("Y", PixelType.Float) }, attributes: CreateSyntheticAttributes());
        Part image = ExrTestHelper.Flat(header, ExrTestHelper.FloatChannel("Y", PixelType.Float, new[] { 1f }));

        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRImageToMemory(image, out byte[] encoded));
        Assert.AreEqual(ExrResult.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out Header? roundTrippedHeader));
        Assert.IsNotNull(roundTrippedHeader);

        foreach (HeaderAttribute expectedAttribute in CreateSyntheticAttributes())
        {
            HeaderAttribute actualAttribute = GetCustomAttribute(roundTrippedHeader!, expectedAttribute.Name);
            AssertAttributeEqual(expectedAttribute, actualAttribute, expectedAttribute.Name);
        }
    }

    private static HeaderAttribute[] CreateSyntheticAttributes()
    {
        return new[]
        {
            new HeaderAttribute("framesPerSecond", "rational", EncodeUInt32Pair(24000u, 1001u)),
            new HeaderAttribute("timeCode", "timecode", EncodeUInt32Pair(0x01020304u, 0xA0B0C0D0u)),
            new HeaderAttribute("keyCode", "keycode", EncodeInt32Sequence(1, 2, 3, 4, 5, 6, 7)),
            new HeaderAttribute("colorInteropID", "string", Encoding.UTF8.GetBytes("urn:tinyexr.net:test\0")),
        };
    }

    private static byte[] EncodeUInt32Pair(uint first, uint second)
    {
        byte[] data = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), first);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), second);
        return data;
    }

    private static byte[] EncodeInt32Sequence(params int[] values)
    {
        byte[] data = new byte[checked(values.Length * sizeof(int))];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(index * sizeof(int), sizeof(int)), values[index]);
        }

        return data;
    }

    private static byte[] EncodeSingles(params float[] values)
    {
        byte[] data = new byte[checked(values.Length * sizeof(float))];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(index * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(values[index]));
        }

        return data;
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

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
    }

    private static HeaderAttribute GetCustomAttribute(Header header, string name)
    {
        HeaderAttribute? attribute = header.Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.Ordinal));
        Assert.IsNotNull(attribute, name);
        return attribute;
    }

    private static HeaderAttribute CloneAttribute(HeaderAttribute attribute)
    {
        return new HeaderAttribute(attribute.Name, attribute.TypeName, attribute.Data);
    }

    private static void AssertAttributeEqual(HeaderAttribute expected, HeaderAttribute actual, string message)
    {
        Assert.AreEqual(expected.Name, actual.Name, message);
        Assert.AreEqual(expected.TypeName, actual.TypeName, message);
        CollectionAssert.AreEqual(expected.Data.ToArray(), actual.Data.ToArray(), message);
    }

    private readonly record struct ExpectedAttributeSample(string RelativePath, string[] AttributeNames);
}
