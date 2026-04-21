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
            (ExrVersion _, ExrHeader header, ExrImage image) = ExrTestHelper.LoadSinglePart(path);
            Dictionary<string, ExrAttribute> expected = sample.AttributeNames.ToDictionary(
                static name => name,
                name => CloneAttribute(GetCustomAttribute(header, name)),
                StringComparer.Ordinal);

            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded), sample.RelativePath);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader roundTrippedHeader), sample.RelativePath);

            foreach ((string attributeName, ExrAttribute expectedAttribute) in expected)
            {
                ExrAttribute actualAttribute = GetCustomAttribute(roundTrippedHeader, attributeName);
                AssertAttributeEqual(expectedAttribute, actualAttribute, $"{sample.RelativePath}|{attributeName}");
            }
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] StandardAttributes|SyntheticRawRoundTrip")]
    public void Case_StandardAttributes_Synthetic_raw_round_trip()
    {
        ExrImage image = CreateTinyRgbImage();
        ExrHeader header = new();
        foreach (ExrAttribute attribute in CreateSyntheticAttributes())
        {
            header.CustomAttributes.Add(attribute);
        }

        Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out byte[] encoded));
        Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader roundTrippedHeader));

        foreach (ExrAttribute expectedAttribute in CreateSyntheticAttributes())
        {
            ExrAttribute actualAttribute = GetCustomAttribute(roundTrippedHeader, expectedAttribute.Name);
            AssertAttributeEqual(expectedAttribute, actualAttribute, expectedAttribute.Name);
        }
    }

    private static ExrImage CreateTinyRgbImage()
    {
        return new ExrImage(
            1,
            1,
            new[]
            {
                ExrTestHelper.FloatChannel("B", ExrPixelType.Float, new[] { 0.25f }),
                ExrTestHelper.FloatChannel("G", ExrPixelType.Float, new[] { 0.5f }),
                ExrTestHelper.FloatChannel("R", ExrPixelType.Float, new[] { 1.0f }),
            });
    }

    private static ExrAttribute[] CreateSyntheticAttributes()
    {
        return new[]
        {
            new ExrAttribute("framesPerSecond", "rational", EncodeUInt32Pair(24000u, 1001u)),
            new ExrAttribute("timeCode", "timecode", EncodeUInt32Pair(0x01020304u, 0xA0B0C0D0u)),
            new ExrAttribute("keyCode", "keycode", EncodeInt32Sequence(1, 2, 3, 4, 5, 6, 7)),
            new ExrAttribute("colorInteropID", "string", Encoding.UTF8.GetBytes("urn:tinyexr.net:test\0")),
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

    private static ExrAttribute GetCustomAttribute(ExrHeader header, string name)
    {
        ExrAttribute? attribute = header.CustomAttributes.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.Ordinal));
        Assert.IsNotNull(attribute, name);
        return attribute;
    }

    private static ExrAttribute CloneAttribute(ExrAttribute attribute)
    {
        return new ExrAttribute(attribute.Name, attribute.TypeName, (byte[])attribute.Value.Clone());
    }

    private static void AssertAttributeEqual(ExrAttribute expected, ExrAttribute actual, string message)
    {
        Assert.AreEqual(expected.Name, actual.Name, message);
        Assert.AreEqual(expected.TypeName, actual.TypeName, message);
        CollectionAssert.AreEqual(expected.Value, actual.Value, message);
    }

    private readonly record struct ExpectedAttributeSample(string RelativePath, string[] AttributeNames);
}
