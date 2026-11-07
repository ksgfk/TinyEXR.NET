namespace TinyEXR.Test;

[TestClass]
public sealed class MultipartCompatibilityTests
{
    [TestMethod(DisplayName = "Multipart unknown type and inconsistent flags are rejected by V3")]
    public void InvalidMultipartMetadata()
    {
        Part part = ExrConvenienceTests.CreatePart(PartType.Scanline, Compression.ZIP, TileLevelMode.OneLevel, "only");
        Assert.AreEqual(ExrResult.Success, Exr.SaveEXRMultipartImageToMemory(new Image(new[] { part }), out byte[] bytes));
        int offset = ExrBinaryMutationHelper.FindHeaderAttributeValueOffset(bytes, 0, "type", "string", out int length);
        bytes.AsSpan(offset, length).Fill((byte)'x');
        using ExrReader reader = ExrReader.OpenMemory(bytes);
        ExrResult expected = reader.ParseHeader().Status;
        Assert.AreNotEqual(ExrResult.Success, expected);
        Assert.AreEqual(expected, Exr.ParseEXRMultipartHeaderFromMemory(bytes, out uint version, out Header[] headers));
        Assert.AreEqual(0U, version);
        Assert.AreEqual(0, headers.Length);
        Assert.AreEqual(expected, Exr.LoadEXRMultipartImageFromMemory(bytes, out Image? image));
        Assert.IsNull(image);
    }
}
