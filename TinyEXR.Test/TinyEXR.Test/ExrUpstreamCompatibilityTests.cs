using System.Collections.Generic;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrUpstreamCompatibilityTests
    {
        [TestMethod]
        [DynamicData(nameof(KnownHeaderCases))]
        public void UpstreamHeadersParseWithExpectedMetadata(
            string samplePath,
            bool regression,
            CompressionType expectedCompression,
            bool expectedTiled,
            bool expectedNonImage,
            bool expectedDeep,
            int expectedWidth,
            int expectedHeight,
            int expectedChannelCount,
            string[] requiredChannels)
        {
            string path = ResolvePath(samplePath, regression);

            ResultCode result = Exr.TryReadHeader(path, out ExrVersion version, out ExrHeader header);
            Assert.AreEqual(ResultCode.Success, result, path);
            Assert.AreEqual(2, version.Version, path);
            Assert.AreEqual(expectedTiled, version.Tiled, path);
            Assert.IsFalse(version.Multipart, path);
            Assert.AreEqual(expectedNonImage, version.NonImage, path);
            Assert.AreEqual(expectedCompression, header.Compression, path);
            Assert.AreEqual(expectedDeep, header.IsDeep, path);
            Assert.AreEqual(expectedWidth, header.DataWindow.Width, path);
            Assert.AreEqual(expectedHeight, header.DataWindow.Height, path);
            Assert.AreEqual(expectedChannelCount, header.Channels.Count, path);
            AssertChannelsPresent(header.Channels, requiredChannels, path);
        }

        [TestMethod]
        [DynamicData(nameof(SupportedDecodeCases))]
        public void SupportedSamplesDecodeToDenseManagedImages(
            string samplePath,
            bool regression,
            int expectedWidth,
            int expectedHeight,
            int expectedChannelCount,
            bool expectedTiled,
            string[] requiredChannels)
        {
            string path = ResolvePath(samplePath, regression);

            ResultCode result = Exr.TryReadImage(path, out ExrHeader header, out ExrImage image);
            Assert.AreEqual(ResultCode.Success, result, path);
            Assert.AreEqual(expectedTiled, header.Tiles != null, path);
            Assert.AreEqual(expectedWidth, image.Width, path);
            Assert.AreEqual(expectedHeight, image.Height, path);
            Assert.AreEqual(expectedChannelCount, image.Channels.Count, path);
            AssertImageChannelsPresent(image.Channels, requiredChannels, path);
        }

        [TestMethod]
        [DynamicData(nameof(UnsupportedDecodeCases))]
        public void UnsupportedOrMultipartSamplesReturnStableReadResults(
            string samplePath,
            bool regression,
            ResultCode expectedResult)
        {
            string path = ResolvePath(samplePath, regression);

            ResultCode result = Exr.TryReadImage(path, out _, out _);
            Assert.AreEqual(expectedResult, result, path);
        }

        [TestMethod]
        [DynamicData(nameof(DisplayWindowCases))]
        public void DisplayWindowHeadersRemainAccurate(
            string samplePath,
            int dataMinX,
            int dataMinY,
            int dataMaxX,
            int dataMaxY,
            int displayMinX,
            int displayMinY,
            int displayMaxX,
            int displayMaxY)
        {
            string path = TestData.Sample(samplePath);

            ResultCode result = Exr.TryReadHeader(path, out ExrHeader header);
            Assert.AreEqual(ResultCode.Success, result, path);
            Assert.AreEqual(new ExrBox2i(dataMinX, dataMinY, dataMaxX, dataMaxY), header.DataWindow, path);
            Assert.AreEqual(new ExrBox2i(displayMinX, displayMinY, displayMaxX, displayMaxY), header.DisplayWindow, path);
        }

        [TestMethod]
        [DynamicData(nameof(TiledHeaderCases))]
        public void TiledHeadersExposeExpectedTileMetadata(
            string samplePath,
            bool regression,
            CompressionType expectedCompression,
            int tileSizeX,
            int tileSizeY,
            ExrTileLevelMode expectedLevelMode,
            ExrTileRoundingMode expectedRoundingMode)
        {
            string path = ResolvePath(samplePath, regression);

            ResultCode result = Exr.TryReadHeader(path, out ExrVersion version, out ExrHeader header);
            Assert.AreEqual(ResultCode.Success, result, path);
            Assert.IsTrue(version.Tiled, path);
            Assert.IsNotNull(header.Tiles, path);
            Assert.AreEqual(expectedCompression, header.Compression, path);
            Assert.AreEqual(tileSizeX, header.Tiles!.TileSizeX, path);
            Assert.AreEqual(tileSizeY, header.Tiles.TileSizeY, path);
            Assert.AreEqual(expectedLevelMode, header.Tiles.LevelMode, path);
            Assert.AreEqual(expectedRoundingMode, header.Tiles.RoundingMode, path);
        }

        [TestMethod]
        [DynamicData(nameof(MultipartCases))]
        public void MultipartSamplesAreDetectedBeforeReadIsRejected(
            string samplePath,
            bool regression,
            bool expectedTiled)
        {
            string path = ResolvePath(samplePath, regression);

            ResultCode versionResult = Exr.TryReadVersion(path, out ExrVersion version);
            Assert.AreEqual(ResultCode.Success, versionResult, path);
            Assert.IsTrue(version.Multipart, path);
            Assert.AreEqual(expectedTiled, version.Tiled, path);
            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.TryReadHeader(path, out _), path);
            Assert.AreEqual(ResultCode.UnsupportedFeature, Exr.TryReadImage(path, out _, out _), path);
        }

        [TestMethod]
        [DynamicData(nameof(RegressionResultCases))]
        public void RegressionFuzzingSamplesReturnStableResultCodes(
            string fileName,
            ResultCode expectedHeaderResult,
            ResultCode expectedImageResult)
        {
            string path = TestData.Regression(fileName);

            ResultCode headerResult = Exr.TryReadHeader(path, out _);
            Assert.AreEqual(expectedHeaderResult, headerResult, path);

            ResultCode imageResult = Exr.TryReadImage(path, out _, out _);
            Assert.AreEqual(expectedImageResult, imageResult, path);
        }

        [TestMethod]
        public void BeachballSinglepartLayersEnumerateAndDecodeLikeUpstreamSinglePartCoverage()
        {
            string sample = TestData.Sample(System.IO.Path.Combine("Beachball", "singlepart.0001.exr"));

            ResultCode layersResult = Exr.TryReadLayers(sample, out string[] layers);
            Assert.AreEqual(ResultCode.Success, layersResult);
            CollectionAssert.AreEqual(
                new[]
                {
                    "disparityL",
                    "disparityR",
                    "forward.left",
                    "forward.right",
                    "left",
                    "whitebarmask.left",
                    "whitebarmask.right",
                },
                layers);

            ResultCode rgbaResult = Exr.TryReadRgba(sample, "left", out float[] rgba, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, rgbaResult);
            Assert.AreEqual(911, width);
            Assert.AreEqual(876, height);
            Assert.AreEqual(width * height * 4, rgba.Length);
            Assert.AreEqual(ResultCode.LayerNotFound, Exr.TryReadRgba(sample, "missing", out _, out _, out _));
        }

        public static IEnumerable<object[]> KnownHeaderCases()
        {
            yield return new object[] { "2by2.exr", true, CompressionType.None, false, false, false, 2, 2, 4, new[] { "A", "B", "G", "R" } };
            yield return new object[] { "flaga.exr", true, CompressionType.ZIP, false, false, false, 128, 64, 8, new[] { "Warstwa 1.R", "Warstwa 2.R" } };
            yield return new object[] { "tiled_half_1x1_alpha.exr", true, CompressionType.None, true, false, false, 1, 1, 1, new[] { "A" } };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "Blobbies.exr"), false, CompressionType.ZIP, false, false, false, 1040, 1040, 5, new[] { "A", "R", "Z" } };
            yield return new object[] { System.IO.Path.Combine("Chromaticities", "Rec709.exr"), false, CompressionType.PIZ, false, false, false, 610, 406, 3, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t08.exr"), false, CompressionType.PIZ, false, false, false, 400, 300, 3, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("Tiles", "GoldenGate.exr"), false, CompressionType.PIZ, true, false, false, 1262, 860, 3, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("Tiles", "Ocean.exr"), false, CompressionType.ZIP, true, false, false, 1255, 876, 3, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("MultiResolution", "Bonita.exr"), false, CompressionType.ZIP, true, false, false, 550, 832, 3, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("MultiResolution", "Kapaa.exr"), false, CompressionType.ZIP, true, false, false, 799, 546, 3, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("Beachball", "singlepart.0001.exr"), false, CompressionType.ZIPS, false, false, false, 911, 876, 20, new[] { "A", "Z", "left.R" } };
            yield return new object[] { System.IO.Path.Combine("v2", "LeftView", "Balls.exr"), false, CompressionType.ZIPS, false, true, true, 1431, 761, 5, new[] { "A", "R", "Z" } };
        }

        public static IEnumerable<object[]> SupportedDecodeCases()
        {
            yield return new object[] { "2by2.exr", true, 2, 2, 4, false, new[] { "A", "B", "G", "R" } };
            yield return new object[] { "tiled_half_1x1_alpha.exr", true, 1, 1, 1, true, new[] { "A" } };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "Blobbies.exr"), false, 1040, 1040, 5, false, new[] { "A", "R", "Z" } };
            yield return new object[] { System.IO.Path.Combine("TestImages", "BrightRings.exr"), false, 800, 800, 3, false, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("TestImages", "BrightRingsNanInf.exr"), false, 800, 800, 3, false, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("TestImages", "WideColorGamut.exr"), false, 800, 800, 3, false, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("Tiles", "Ocean.exr"), false, 1255, 876, 3, true, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("MultiResolution", "Bonita.exr"), false, 550, 832, 3, true, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("MultiResolution", "Kapaa.exr"), false, 799, 546, 3, true, new[] { "B", "G", "R" } };
            yield return new object[] { System.IO.Path.Combine("Beachball", "singlepart.0001.exr"), false, 911, 876, 20, false, new[] { "A", "left.R", "whitebarmask.right.mask" } };
        }

        public static IEnumerable<object[]> UnsupportedDecodeCases()
        {
            yield return new object[] { System.IO.Path.Combine("ScanLines", "CandleGlass.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "Desk.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "MtTamWest.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "PrismsLenses.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "StillLife.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("ScanLines", "Tree.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("Chromaticities", "Rec709.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("Chromaticities", "Rec709_YC.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("Chromaticities", "XYZ.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("Chromaticities", "XYZ_YC.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("TestImages", "AllHalfValues.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("TestImages", "GammaChart.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("LuminanceChroma", "MtTamNorth.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("LuminanceChroma", "StarField.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("LuminanceChroma", "Garden.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("LuminanceChroma", "CrissyField.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t01.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t08.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t16.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("Tiles", "GoldenGate.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("Tiles", "Spirals.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { System.IO.Path.Combine("MultiView", "Adjuster.exr"), false, ResultCode.UnsupportedFeature };
            yield return new object[] { "000-issue194.exr", true, ResultCode.UnsupportedFeature };
            yield return new object[] { "issue-160-piz-decode.exr", true, ResultCode.UnsupportedFeature };
            yield return new object[] { "piz-bug-issue-100.exr", true, ResultCode.UnsupportedFeature };
            yield return new object[] { "poc-255456016cca60ddb5c5ed6898182e13739bf687b17d1411e97bb60ad95e7a84_min", true, ResultCode.UnsupportedFeature };
            yield return new object[] { "poc-360c3b0555cb979ca108f2d178cf8a80959cfeabaa4ec1d310d062fa653a8c6b_min", true, ResultCode.UnsupportedFeature };
            yield return new object[] { "poc-3f1f642c3356fd8e8d2a0787613ec09a56572b3a1e38c9629b6db9e8dead1117_min", true, ResultCode.UnsupportedFeature };
            yield return new object[] { "poc-5b66774a7498c635334ad386be0c3b359951738ac47f14878a3346d1c6ea0fe5_min", true, ResultCode.UnsupportedFeature };
        }

        public static IEnumerable<object[]> DisplayWindowCases()
        {
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t01.exr"), 0, 0, 399, 299, 0, 0, 399, 299 };
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t08.exr"), 30, 40, 429, 339, 0, 0, 500, 400 };
            yield return new object[] { System.IO.Path.Combine("DisplayWindow", "t16.exr"), 0, 0, 399, 299, -40, -40, 440, 330 };
        }

        public static IEnumerable<object[]> TiledHeaderCases()
        {
            yield return new object[] { "tiled_half_1x1_alpha.exr", true, CompressionType.None, 1, 1, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown };
            yield return new object[] { System.IO.Path.Combine("Tiles", "GoldenGate.exr"), false, CompressionType.PIZ, 128, 128, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown };
            yield return new object[] { System.IO.Path.Combine("Tiles", "Ocean.exr"), false, CompressionType.ZIP, 128, 128, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown };
            yield return new object[] { System.IO.Path.Combine("MultiResolution", "Bonita.exr"), false, CompressionType.ZIP, 128, 128, ExrTileLevelMode.MipMapLevels, ExrTileRoundingMode.RoundDown };
            yield return new object[] { System.IO.Path.Combine("MultiResolution", "Kapaa.exr"), false, CompressionType.ZIP, 64, 64, ExrTileLevelMode.RipMapLevels, ExrTileRoundingMode.RoundUp };
            yield return new object[] { System.IO.Path.Combine("Tiles", "Spirals.exr"), false, CompressionType.PXR24, 287, 126, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown };
            yield return new object[] { System.IO.Path.Combine("LuminanceChroma", "Garden.exr"), false, CompressionType.PIZ, 128, 128, ExrTileLevelMode.OneLevel, ExrTileRoundingMode.RoundDown };
        }

        public static IEnumerable<object[]> MultipartCases()
        {
            yield return new object[] { System.IO.Path.Combine("Beachball", "multipart.0001.exr"), false, false };
            yield return new object[] { "issue-238-double-free-multipart.exr", true, false };
            yield return new object[] { "poc-5ace655ef080932dcc7e4abc9eab1d4f82c845453464993dfa3eb6c5822a1621", true, true };
        }

        public static IEnumerable<object[]> RegressionResultCases()
        {
            yield return new object[] { "issue-238-double-free.exr", ResultCode.Success, ResultCode.InvalidData };
            yield return new object[] { "poc-1383755b301e5f505b2198dc0508918b537fdf48bbfc6deeffe268822e6f6cd6", ResultCode.Success, ResultCode.InvalidData };
            yield return new object[] { "poc-24322747c47e87a10e4407528b779a1a763a48135384909b3d1010bbba1d4c28_min", ResultCode.Success, ResultCode.InvalidData };
            yield return new object[] { "poc-d5c9c893e559277a3320c196523095b94db93985620ac338d037487e0e613047_min", ResultCode.UnsupportedFormat, ResultCode.UnsupportedFormat };
            yield return new object[] { "poc-df76d1f27adb8927a1446a603028272140905c168a336128465a1162ec7af270.mini", ResultCode.InvalidData, ResultCode.InvalidData };
            yield return new object[] { "poc-e7fa6404daa861369d2172fe68e08f9d38c0989f57da7bcfb510bab67e19ca9f", ResultCode.Success, ResultCode.InvalidData };
            yield return new object[] { "poc-eedff3a9e99eb1c0fd3a3b0989e7c44c0a69f04f10b23e5264f362a4773f4397_min", ResultCode.InvalidHeader, ResultCode.InvalidHeader };
            yield return new object[] { "poc-efe9007bfdcbbe8a1569bf01fa9acadb8261ead49cb83f6e91fcdc4dae2e99a3_min", ResultCode.InvalidData, ResultCode.InvalidData };
        }

        private static string ResolvePath(string samplePath, bool regression)
        {
            return regression ? TestData.Regression(samplePath) : TestData.Sample(samplePath);
        }

        private static void AssertChannelsPresent(IList<ExrChannel> channels, string[] requiredChannels, string samplePath)
        {
            HashSet<string> channelNames = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ExrChannel channel in channels)
            {
                channelNames.Add(channel.Name);
            }

            foreach (string channelName in requiredChannels)
            {
                Assert.IsTrue(channelNames.Contains(channelName), $"Channel '{channelName}' was not found in '{samplePath}'.");
            }
        }

        private static void AssertImageChannelsPresent(IList<ExrImageChannel> channels, string[] requiredChannels, string samplePath)
        {
            HashSet<string> channelNames = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ExrImageChannel channel in channels)
            {
                channelNames.Add(channel.Channel.Name);
            }

            foreach (string channelName in requiredChannels)
            {
                Assert.IsTrue(channelNames.Contains(channelName), $"Image channel '{channelName}' was not found in '{samplePath}'.");
            }
        }
    }
}
