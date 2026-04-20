using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrWriterParityTests
    {
        [TestMethod]
        [DynamicData(nameof(MultiResolutionWriterCases))]
        public void SavingMultiResolutionRoundTripsLikeUpstreamWriterCoverage(string samplePath)
        {
            string path = TestData.Sample(samplePath);

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version1));
            Assert.IsTrue(version1.Tiled);
            Assert.IsFalse(version1.Multipart);

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header1));
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(path, header1, out ExrImage image1));
            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image1, header1, out byte[] encoded));

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out ExrVersion version2, out ExrHeader header2));
            Assert.IsTrue(version2.Tiled);
            Assert.IsFalse(version2.Multipart);
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, header2, out ExrImage image2));

            CompareHeadersLikeUpstream(header1, header2);
            CompareImagesLikeUpstream(image1, image2);
        }

        [TestMethod]
        public void SavingMultipartRoundTripsLikeUpstreamWriterCoverage()
        {
            string path = TestData.Sample(Path.Combine("Beachball", "multipart.0001.exr"));

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRVersionFromFile(path, out ExrVersion version1));
            Assert.IsTrue(version1.Multipart);

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromFile(path, out _, out ExrMultipartHeader headers1));
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromFile(path, headers1, out ExrMultipartImage images1));
            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRMultipartImageToMemory(images1, headers1, out byte[] encoded));

            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRMultipartHeaderFromMemory(encoded, out ExrVersion version2, out ExrMultipartHeader headers2));
            Assert.IsTrue(version2.Multipart);
            Assert.AreEqual(headers1.Headers.Count, headers2.Headers.Count);

            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRMultipartImageFromMemory(encoded, headers2, out ExrMultipartImage images2));
            Assert.AreEqual(images1.Images.Count, images2.Images.Count);

            for (int i = 0; i < headers1.Headers.Count; i++)
            {
                CompareHeadersLikeUpstream(headers1.Headers[i], headers2.Headers[i]);
                CompareImagesLikeUpstream(images1.Images[i], images2.Images[i]);
            }
        }

        [TestMethod]
        public void MultiLevelManagedImageWithoutTileHeaderIsRejected()
        {
            ExrImage image = new ExrImage(
                new[]
                {
                    new ExrImageLevel(
                        levelX: 0,
                        levelY: 0,
                        width: 2,
                        height: 2,
                        channels: new[]
                        {
                            new ExrImageChannel(new ExrChannel("R", ExrPixelType.Float), ExrPixelType.Float, ToFloatBytes(1.0f, 2.0f, 3.0f, 4.0f)),
                        }),
                    new ExrImageLevel(
                        levelX: 1,
                        levelY: 1,
                        width: 1,
                        height: 1,
                        channels: new[]
                        {
                            new ExrImageChannel(new ExrChannel("R", ExrPixelType.Float), ExrPixelType.Float, ToFloatBytes(9.0f)),
                        }),
                });

            Assert.AreEqual(
                ResultCode.InvalidArgument,
                Exr.SaveEXRImageToMemory(
                    image,
                    new ExrHeader
                    {
                        Compression = CompressionType.None,
                    },
                    out _));
        }

        public static IEnumerable<object[]> MultiResolutionWriterCases()
        {
            yield return new object[] { Path.Combine("MultiResolution", "Bonita.exr") };
            yield return new object[] { Path.Combine("MultiResolution", "Kapaa.exr") };
        }

        private static void CompareHeadersLikeUpstream(ExrHeader expected, ExrHeader actual)
        {
            Assert.AreEqual(expected.Compression, actual.Compression);
            Assert.AreEqual(expected.Channels.Count, actual.Channels.Count);
            Assert.AreEqual(expected.DataWindow.Width, actual.DataWindow.Width);
            Assert.AreEqual(expected.DataWindow.Height, actual.DataWindow.Height);
            Assert.AreEqual(expected.Tiles != null, actual.Tiles != null);

            if (expected.Tiles != null || actual.Tiles != null)
            {
                Assert.IsNotNull(expected.Tiles);
                Assert.IsNotNull(actual.Tiles);
                Assert.AreEqual(expected.Tiles!.TileSizeX, actual.Tiles!.TileSizeX);
                Assert.AreEqual(expected.Tiles.TileSizeY, actual.Tiles.TileSizeY);
                Assert.AreEqual(expected.Tiles.LevelMode, actual.Tiles.LevelMode);
                Assert.AreEqual(expected.Tiles.RoundingMode, actual.Tiles.RoundingMode);
            }

            Assert.AreEqual(expected.IsDeep, actual.IsDeep);
            Assert.AreEqual(expected.Name ?? string.Empty, actual.Name ?? string.Empty);

            for (int i = 0; i < expected.Channels.Count; i++)
            {
                Assert.AreEqual(expected.Channels[i].RequestedPixelType, actual.Channels[i].RequestedPixelType);
                Assert.AreEqual(expected.Channels[i].Name, actual.Channels[i].Name);
                Assert.AreEqual(expected.Channels[i].Type, actual.Channels[i].Type);
            }
        }

        private static void CompareImagesLikeUpstream(ExrImage expected, ExrImage actual)
        {
            bool expectedHasTiles = expected.Levels[0].Tiles.Count > 0;
            bool actualHasTiles = actual.Levels[0].Tiles.Count > 0;
            Assert.AreEqual(expectedHasTiles, actualHasTiles);
            Assert.AreEqual(expected.Channels.Count, actual.Channels.Count);
            Assert.AreEqual(expected.Levels.Count, actual.Levels.Count);

            for (int i = 0; i < expected.Levels.Count; i++)
            {
                ExrImageLevel expectedLevel = expected.Levels[i];
                ExrImageLevel actualLevel = actual.Levels[i];
                Assert.AreEqual(expectedLevel.LevelX, actualLevel.LevelX);
                Assert.AreEqual(expectedLevel.LevelY, actualLevel.LevelY);
                Assert.AreEqual(expectedLevel.Width, actualLevel.Width);
                Assert.AreEqual(expectedLevel.Height, actualLevel.Height);
                Assert.AreEqual(expectedLevel.Tiles.Count, actualLevel.Tiles.Count);
            }
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
    }
}
