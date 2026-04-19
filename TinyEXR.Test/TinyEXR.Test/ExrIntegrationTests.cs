using System;
using System.IO;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrIntegrationTests
    {
        [TestMethod]
        public void LayerEnumerationAndLayerSelectionWork()
        {
            string layeredSample = TestData.Regression("flaga.exr");

            ResultCode layersResult = Exr.EXRLayers(layeredSample, out string[] layers);
            Assert.AreEqual(ResultCode.Success, layersResult);
            CollectionAssert.AreEqual(new[] { "Warstwa 1", "Warstwa 2" }, layers);

            Assert.AreEqual(
                ResultCode.LayerNotFound,
                Exr.LoadEXR(layeredSample, out _, out _, out _));

            ResultCode layerLoadResult = Exr.LoadEXRWithLayer(
                layeredSample,
                "Warstwa 1",
                out float[] rgba,
                out int width,
                out int height);
            Assert.AreEqual(ResultCode.Success, layerLoadResult);
            Assert.AreEqual(128, width);
            Assert.AreEqual(64, height);
            Assert.AreEqual(width * height * 4, rgba.Length);

            Assert.AreEqual(
                ResultCode.LayerNotFound,
                Exr.LoadEXRWithLayer(layeredSample, "missing", out _, out _, out _));
        }

        [TestMethod]
        public void ZipAndZipsScanlinesDecode()
        {
            AssertRgbaReadSucceeds(
                TestData.Sample(Path.Combine("ScanLines", "Blobbies.exr")),
                1040,
                1040);

            AssertRgbaReadSucceeds(
                TestData.Sample(Path.Combine("Beachball", "singlepart.0001.exr")),
                911,
                876);
        }

        [TestMethod]
        public void TiledAndMultiResolutionImagesDecode()
        {
            AssertTiledImageReadSucceeds(
                TestData.Sample(Path.Combine("Tiles", "Ocean.exr")),
                ExrTileLevelMode.OneLevel,
                ExrTileRoundingMode.RoundDown,
                1255,
                876);

            AssertTiledImageReadSucceeds(
                TestData.Sample(Path.Combine("MultiResolution", "Bonita.exr")),
                ExrTileLevelMode.MipMapLevels,
                ExrTileRoundingMode.RoundDown,
                550,
                832);

            AssertTiledImageReadSucceeds(
                TestData.Sample(Path.Combine("MultiResolution", "Kapaa.exr")),
                ExrTileLevelMode.RipMapLevels,
                ExrTileRoundingMode.RoundUp,
                799,
                546);

            AssertTiledImageReadSucceeds(
                TestData.Regression("tiled_half_1x1_alpha.exr"),
                ExrTileLevelMode.OneLevel,
                ExrTileRoundingMode.RoundDown,
                1,
                1);
        }

        [TestMethod]
        public void DeepImagesDecode()
        {
            string deepSample = TestData.Sample(Path.Combine("v2", "LeftView", "Balls.exr"));

            ResultCode headerResult = Exr.TryReadHeader(deepSample, out ExrVersion version, out ExrHeader header);
            Assert.AreEqual(ResultCode.Success, headerResult);
            Assert.IsFalse(version.Multipart);
            Assert.IsTrue(header.IsDeep);

            ResultCode deepResult = Exr.TryReadDeepImage(deepSample, out ExrHeader deepHeader, out ExrDeepImage image);
            Assert.AreEqual(ResultCode.Success, deepResult);
            Assert.IsTrue(deepHeader.IsDeep);
            Assert.AreEqual(1431, image.Width);
            Assert.AreEqual(761, image.Height);
            Assert.AreEqual(deepHeader.Channels.Count, image.Channels.Count);
            Assert.AreEqual(image.Height, image.OffsetTable.Length);
            Assert.IsTrue(image.OffsetTable[0].Length > 0);
            Assert.IsTrue(image.Channels[0].Rows[0].Length >= 0);
            Assert.AreEqual(
                ResultCode.UnsupportedFeature,
                Exr.TryReadImage(deepSample, out _, out _));
        }

        [TestMethod]
        public void MultipartVersionIsDetectedBeforeReadIsRejected()
        {
            string multipart = TestData.Sample(Path.Combine("Beachball", "multipart.0001.exr"));

            ResultCode versionResult = Exr.TryReadVersion(multipart, out ExrVersion version);
            Assert.AreEqual(ResultCode.Success, versionResult);
            Assert.IsTrue(version.Multipart);

            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.TryReadHeader(multipart, out _));
            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.TryReadImage(multipart, out _, out _));
        }

        [TestMethod]
        public void UnsupportedCompressedSamplesAreReported()
        {
            AssertUnsupportedRead(TestData.Regression("poc-255456016cca60ddb5c5ed6898182e13739bf687b17d1411e97bb60ad95e7a84_min"));
            AssertUnsupportedRead(TestData.Sample(Path.Combine("ScanLines", "Desk.exr")));
            AssertUnsupportedRead(TestData.Sample(Path.Combine("TestImages", "GammaChart.exr")));
            AssertUnsupportedRead(TestData.Sample(Path.Combine("LuminanceChroma", "CrissyField.exr")));
            AssertUnsupportedRead(TestData.Sample(Path.Combine("MultiView", "Adjuster.exr")));
        }

        private static void AssertRgbaReadSucceeds(string path, int expectedWidth, int expectedHeight)
        {
            ResultCode result = Exr.LoadEXR(path, out float[] rgba, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, result, $"Failed to decode '{path}'.");
            Assert.AreEqual(expectedWidth, width);
            Assert.AreEqual(expectedHeight, height);
            Assert.AreEqual(width * height * 4, rgba.Length);
        }

        private static void AssertTiledImageReadSucceeds(
            string path,
            ExrTileLevelMode expectedLevelMode,
            ExrTileRoundingMode expectedRoundingMode,
            int expectedWidth,
            int expectedHeight)
        {
            ResultCode headerResult = Exr.TryReadHeader(path, out ExrHeader header);
            Assert.AreEqual(ResultCode.Success, headerResult, $"Failed to parse header '{path}'.");
            Assert.IsNotNull(header.Tiles);
            Assert.AreEqual(expectedLevelMode, header.Tiles!.LevelMode);
            Assert.AreEqual(expectedRoundingMode, header.Tiles.RoundingMode);

            ResultCode imageResult = Exr.TryReadImage(path, out ExrHeader decodedHeader, out ExrImage image);
            Assert.AreEqual(ResultCode.Success, imageResult, $"Failed to decode '{path}'.");
            Assert.IsNotNull(decodedHeader.Tiles);
            Assert.AreEqual(expectedWidth, image.Width);
            Assert.AreEqual(expectedHeight, image.Height);
            Assert.AreEqual(decodedHeader.Channels.Count, image.Channels.Count);
        }

        private static void AssertUnsupportedRead(string path)
        {
            ResultCode result = Exr.TryReadImage(path, out _, out _);
            Assert.AreEqual(ResultCode.UnsupportedFeature, result, $"Expected unsupported compression for '{path}'.");
        }
    }
}
