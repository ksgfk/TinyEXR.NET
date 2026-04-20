using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrManagedFacadeTests
    {
        [TestMethod]
        public void RequestedPixelTypeOverridesAreAppliedWhenLoadingManagedImage()
        {
            ExrHeader writeHeader = new ExrHeader
            {
                Compression = CompressionType.None,
            };
            ExrImage sourceImage = new ExrImage(
                width: 2,
                height: 1,
                channels: new[]
                {
                    new ExrImageChannel(new ExrChannel("B", ExrPixelType.Half), ExrPixelType.Float, ToFloatBytes(0.25f, 0.75f)),
                    new ExrImageChannel(new ExrChannel("G", ExrPixelType.Half), ExrPixelType.Float, ToFloatBytes(0.5f, 0.125f)),
                    new ExrImageChannel(new ExrChannel("R", ExrPixelType.Half), ExrPixelType.Float, ToFloatBytes(1.0f, 0.0f)),
                });

            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(sourceImage, writeHeader, out byte[] encoded));
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader parsedHeader));
            foreach (ExrChannel channel in parsedHeader.Channels)
            {
                channel.RequestedPixelType = ExrPixelType.Float;
            }

            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, parsedHeader, out ExrImage decodedImage));
            CollectionAssert.AreEqual(
                new[] { ExrPixelType.Float, ExrPixelType.Float, ExrPixelType.Float },
                decodedImage.Channels.Select(static channel => channel.DataType).ToArray());
            TestHelpers.AssertFloatSequence(new[] { 0.25f, 0.75f }, ReadFloatChannel(decodedImage.GetChannel("B")), 0.001f);
            TestHelpers.AssertFloatSequence(new[] { 0.5f, 0.125f }, ReadFloatChannel(decodedImage.GetChannel("G")), 0.001f);
            TestHelpers.AssertFloatSequence(new[] { 1.0f, 0.0f }, ReadFloatChannel(decodedImage.GetChannel("R")), 0.001f);
        }

        [TestMethod]
        public void MultipartFacadeParsesAndLoadsBeachballFromFile()
        {
            string path = TestData.Sample(Path.Combine("Beachball", "multipart.0001.exr"));

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version));
            Assert.IsTrue(version.Multipart);
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out ExrVersion parsedVersion, out ExrMultipartHeader headers));
            Assert.IsTrue(parsedVersion.Multipart);
            Assert.AreEqual(10, headers.Headers.Count);
            Assert.IsTrue(headers.Headers.All(static header => !string.IsNullOrWhiteSpace(header.Name)));

            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromFile(path, headers, out ExrMultipartImage images));
            Assert.AreEqual(headers.Headers.Count, images.Images.Count);
            for (int i = 0; i < headers.Headers.Count; i++)
            {
                Assert.AreEqual(headers.Headers[i].DataWindow.Width, images.Images[i].Width, $"Width mismatch for part {i}.");
                Assert.AreEqual(headers.Headers[i].DataWindow.Height, images.Images[i].Height, $"Height mismatch for part {i}.");
                Assert.AreEqual(headers.Headers[i].Channels.Count, images.Images[i].Channels.Count, $"Channel count mismatch for part {i}.");
            }
        }

        [TestMethod]
        public void MultipartFacadeParsesAndLoadsBeachballFromMemory()
        {
            byte[] data = File.ReadAllBytes(TestData.Sample(Path.Combine("Beachball", "multipart.0001.exr")));

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(data, out ExrVersion version, out ExrMultipartHeader headers));
            Assert.IsTrue(version.Multipart);
            Assert.AreEqual(10, headers.Headers.Count);

            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromMemory(data, headers, out ExrMultipartImage images));
            Assert.AreEqual(headers.Headers.Count, images.Images.Count);
            Assert.IsTrue(images.Images.All(static image => image.Width > 0 && image.Height > 0));
        }

        private static byte[] ToFloatBytes(params float[] values)
        {
            byte[] data = new byte[values.Length * sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(values[i]));
            }

            return data;
        }

        private static float[] ReadFloatChannel(ExrImageChannel channel)
        {
            Assert.AreEqual(ExrPixelType.Float, channel.DataType);
            float[] values = new float[channel.Data.Length / sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(channel.Data.AsSpan(i * sizeof(float), sizeof(float))));
            }

            return values;
        }
    }
}
