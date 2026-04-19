using System;
using System.Buffers.Binary;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class NetStandardFallbackTests
    {
        [TestMethod]
        public void NetStandardReadsUncompressedScanlineAndTileSamples()
        {
            Assert.AreEqual(
                ResultCode.Success,
                Exr.LoadEXR(TestData.Regression("2by2.exr"), out float[] rgba, out int width, out int height));
            Assert.AreEqual(2, width);
            Assert.AreEqual(2, height);
            Assert.AreEqual(16, rgba.Length);

            Assert.AreEqual(
                ResultCode.Success,
                Exr.TryReadImage(TestData.Regression("tiled_half_1x1_alpha.exr"), out _, out ExrImage tiled));
            Assert.AreEqual(1, tiled.Width);
            Assert.AreEqual(1, tiled.Height);
            Assert.AreEqual(1, tiled.Channels.Count);
        }

        [TestMethod]
        public void NetStandardRejectsZipAndZipsReads()
        {
            Assert.AreEqual(
                ResultCode.UnsupportedFeature,
                Exr.TryReadImage(TestData.Sample(System.IO.Path.Combine("ScanLines", "Blobbies.exr")), out _, out _));

            Assert.AreEqual(
                ResultCode.UnsupportedFeature,
                Exr.TryReadImage(TestData.Sample(System.IO.Path.Combine("Beachball", "singlepart.0001.exr")), out _, out _));
        }

        [TestMethod]
        public void NetStandardRejectsZipWriterPathsButSupportsNone()
        {
            float[] large = CreateSolidRgba(width: 32, height: 32, value: 0.5f);
            Assert.AreEqual(
                ResultCode.UnsupportedFeature,
                Exr.SaveEXRToMemory(large, 32, 32, 4, asFp16: false, out _));

            float[] tiny = CreateSolidRgba(width: 1, height: 1, value: 1.0f);
            ResultCode saveResult = Exr.SaveEXRToMemory(tiny, 1, 1, 4, asFp16: false, out byte[] encoded);
            Assert.AreEqual(ResultCode.Success, saveResult);
            Assert.IsTrue(Exr.IsExrFromMemory(encoded));

            ScanlineExrWriter writer = new ScanlineExrWriter()
                .SetSize(2, 2)
                .SetCompression(CompressionType.None)
                .AddChannel("A", ExrPixelType.Float, ToBytes(new[] { 1.0f, 1.0f, 1.0f, 1.0f }), ExrPixelType.Float)
                .AddChannel("B", ExrPixelType.Float, ToBytes(new[] { 0.0f, 0.0f, 0.0f, 0.0f }), ExrPixelType.Float)
                .AddChannel("G", ExrPixelType.Float, ToBytes(new[] { 0.0f, 1.0f, 0.0f, 1.0f }), ExrPixelType.Float)
                .AddChannel("R", ExrPixelType.Float, ToBytes(new[] { 1.0f, 0.0f, 1.0f, 0.0f }), ExrPixelType.Float);

            byte[] writerEncoded = writer.Save();
            Assert.IsTrue(Exr.IsExrFromMemory(writerEncoded));

            ScanlineExrWriter zipWriter = new ScanlineExrWriter()
                .SetSize(32, 32)
                .AddChannel("A", ExrPixelType.Float, ToBytes(new float[32 * 32]), ExrPixelType.Float)
                .AddChannel("B", ExrPixelType.Float, ToBytes(new float[32 * 32]), ExrPixelType.Float)
                .AddChannel("G", ExrPixelType.Float, ToBytes(new float[32 * 32]), ExrPixelType.Float)
                .AddChannel("R", ExrPixelType.Float, ToBytes(new float[32 * 32]), ExrPixelType.Float);

            Assert.ThrowsExactly<NotSupportedException>(() => zipWriter.Save());
        }

        private static float[] CreateSolidRgba(int width, int height, float value)
        {
            float[] rgba = new float[width * height * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i + 0] = value;
                rgba[i + 1] = value;
                rgba[i + 2] = value;
                rgba[i + 3] = 1.0f;
            }

            return rgba;
        }

        private static byte[] ToBytes(float[] values)
        {
            byte[] data = new byte[values.Length * sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(values[i]));
            }

            return data;
        }
    }
}
