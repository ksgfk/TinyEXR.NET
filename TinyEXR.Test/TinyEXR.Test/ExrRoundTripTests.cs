using System;
using System.Buffers.Binary;
using System.IO;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrRoundTripTests
    {
        [TestMethod]
        public void SaveExrToMemoryAndFileRoundTrip()
        {
            float[] rgba = CreateCheckerboard(width: 32, height: 24);

            ResultCode memoryResult = Exr.SaveEXRToMemory(rgba, 32, 24, 4, asFp16: false, out byte[] encoded);
            Assert.AreEqual(ResultCode.Success, memoryResult);
            Assert.IsTrue(Exr.IsExrFromMemory(encoded));

            ResultCode loadResult = Exr.LoadEXRFromMemory(encoded, out float[] decoded, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, loadResult);
            Assert.AreEqual(32, width);
            Assert.AreEqual(24, height);
            TestHelpers.AssertFloatSequence(rgba, decoded, 0.0001f);

            using TemporaryDirectory tempDirectory = new TemporaryDirectory();
            string outputPath = Path.Combine(tempDirectory.Path, "checker.exr");
            ResultCode fileResult = Exr.SaveEXR(rgba, 32, 24, 4, asFp16: false, outputPath);
            Assert.AreEqual(ResultCode.Success, fileResult);
            Assert.IsTrue(File.Exists(outputPath));
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXR(outputPath, out decoded, out width, out height));
            TestHelpers.AssertFloatSequence(rgba, decoded, 0.0001f);
        }

        [TestMethod]
        public void SaveExrToMemoryRoundTripsFp16Payloads()
        {
            float[] rgba = CreateCheckerboard(width: 17, height: 9);

            ResultCode saveResult = Exr.SaveEXRToMemory(rgba, 17, 9, 4, asFp16: true, out byte[] encoded);
            Assert.AreEqual(ResultCode.Success, saveResult);
            Assert.IsTrue(Exr.IsExrFromMemory(encoded));

            ResultCode loadResult = Exr.LoadEXRFromMemory(encoded, out float[] decoded, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, loadResult);
            Assert.AreEqual(17, width);
            Assert.AreEqual(9, height);
            TestHelpers.AssertFloatSequence(rgba, decoded, 0.001f);
        }

        [TestMethod]
        public void ScanlineWriterRoundTripsThroughReader()
        {
            float[] r =
            {
                1.0f, 0.0f,
                0.0f, 1.0f,
            };
            float[] g =
            {
                0.0f, 1.0f,
                0.5f, 0.25f,
            };
            float[] b =
            {
                0.25f, 0.5f,
                1.0f, 0.0f,
            };
            float[] a =
            {
                1.0f, 1.0f,
                0.5f, 0.25f,
            };

            ScanlineExrWriter writer = new ScanlineExrWriter()
                .SetSize(2, 2)
                .SetCompression(CompressionType.None)
                .AddChannel("A", ExrPixelType.Float, ToBytes(a), ExrPixelType.Float)
                .AddChannel("B", ExrPixelType.Float, ToBytes(b), ExrPixelType.Float)
                .AddChannel("G", ExrPixelType.Float, ToBytes(g), ExrPixelType.Float)
                .AddChannel("R", ExrPixelType.Float, ToBytes(r), ExrPixelType.Float);

            byte[] encoded = writer.Save();
            Assert.IsTrue(Exr.IsExrFromMemory(encoded));

            SinglePartExrReader reader = new SinglePartExrReader();
            reader.Read(encoded);
            Assert.AreEqual(2, reader.Width);
            Assert.AreEqual(2, reader.Height);
            CollectionAssert.AreEqual(new[] { "A", "B", "G", "R" }, GetChannelNames(reader.Channels));

            ResultCode loadResult = Exr.LoadEXRFromMemory(encoded, out float[] rgba, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, loadResult);
            Assert.AreEqual(2, width);
            Assert.AreEqual(2, height);
            TestHelpers.AssertFloatSequence(
                new[]
                {
                    1.0f, 0.0f, 0.25f, 1.0f,
                    0.0f, 1.0f, 0.5f, 1.0f,
                    0.0f, 0.5f, 1.0f, 0.5f,
                    1.0f, 0.25f, 0.0f, 0.25f,
                },
                rgba,
                0.0001f);
        }

        private static float[] CreateCheckerboard(int width, int height)
        {
            float[] rgba = new float[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 4;
                    bool on = ((x / 4) + (y / 4)) % 2 == 0;
                    float value = on ? 1.0f : 0.0f;
                    rgba[index + 0] = value;
                    rgba[index + 1] = on ? 0.25f : 0.75f;
                    rgba[index + 2] = on ? 0.5f : 0.125f;
                    rgba[index + 3] = 1.0f;
                }
            }

            return rgba;
        }

        private static byte[] ToBytes(float[] values)
        {
            byte[] data = new byte[values.Length * sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    data.AsSpan(i * sizeof(float), sizeof(float)),
                    BitConverter.SingleToInt32Bits(values[i]));
            }

            return data;
        }

        private static string[] GetChannelNames(ExrChannel[] channels)
        {
            string[] names = new string[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                names[i] = channels[i].Name;
            }

            return names;
        }
    }
}
