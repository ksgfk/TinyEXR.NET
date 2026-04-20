using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrApiShapeTests
    {
        [TestMethod]
        public void ExrPublicSurfaceUsesOnlyTinyExrNamedMethods()
        {
            string[] publicMethodNames = typeof(Exr)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(static method => !method.IsSpecialName)
                .Select(static method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            string[] expected =
            {
                "EXRLayers",
                "EXRFormatWavelength",
                "EXRGetSpectralUnits",
                "EXRGetSpectrumType",
                "EXRGetStokesComponent",
                "EXRGetWavelengths",
                "EXRIsSpectralChannel",
                "EXRNumLevels",
                "EXRParseSpectralChannelWavelength",
                "EXRReflectiveChannelName",
                "EXRSetNameAttr",
                "EXRSetSpectralAttributes",
                "EXRSpectralChannelName",
                "IsEXR",
                "IsEXRFromMemory",
                "IsSpectralEXR",
                "IsSpectralEXRFromMemory",
                "LoadDeepEXR",
                "LoadEXR",
                "LoadEXRFromMemory",
                "LoadEXRImageFromFile",
                "LoadEXRImageFromMemory",
                "LoadEXRMultipartImageFromFile",
                "LoadEXRMultipartImageFromMemory",
                "LoadEXRWithLayer",
                "ParseEXRHeaderFromFile",
                "ParseEXRHeaderFromMemory",
                "ParseEXRMultipartHeaderFromFile",
                "ParseEXRMultipartHeaderFromMemory",
                "ParseEXRVersionFromFile",
                "ParseEXRVersionFromMemory",
                "SaveEXR",
                "SaveEXRImageToFile",
                "SaveEXRImageToMemory",
                "SaveEXRMultipartImageToFile",
                "SaveEXRMultipartImageToMemory",
                "SaveEXRToMemory",
            };

            CollectionAssert.AreEquivalent(expected, publicMethodNames);
        }

        [TestMethod]
        public void UpstreamActiveCaseManifestMatchesCurrentTesterInventory()
        {
            IReadOnlyList<UpstreamCaseManifestEntry> activeCases = UpstreamCaseManifest.ActiveCases;
            Assert.AreEqual(56, activeCases.Count, "tester.cc currently has 56 active TEST_CASE entries; the earlier 55 count was stale.");
            Assert.AreEqual(activeCases.Count, activeCases.Select(static entry => entry.UpstreamCaseName).Distinct(StringComparer.Ordinal).Count());
            Assert.AreEqual(activeCases.Count, activeCases.Select(static entry => entry.CSharpCaseName).Distinct(StringComparer.Ordinal).Count());
            CollectionAssert.AreEqual(
                activeCases.Select(static entry => entry.SourceLine).OrderBy(static line => line).ToArray(),
                activeCases.Select(static entry => entry.SourceLine).ToArray());
            Assert.IsTrue(activeCases.All(static entry => entry.ApplicableTfm == "net10.0" && !entry.IsFeatureCompletion));
        }

        [TestMethod]
        public void FeatureCompletionManifestTracksDisabledOrCommentedUpstreamCoverage()
        {
            IReadOnlyList<UpstreamCaseManifestEntry> featureCases = UpstreamCaseManifest.FeatureCompletionCases;
            Assert.IsTrue(featureCases.All(static entry => entry.IsFeatureCompletion));
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "Tiles/Spirals.exr",
                    "ScanLines/Cannon.exr",
                    "TestImages/GammaChart.exr",
                    "LuminanceChroma/CrissyField.exr",
                },
                featureCases.Select(static entry => entry.UpstreamCaseName).ToArray());
        }

        [TestMethod]
        public void TinyExrNamedFacadeSupportsManagedSingleImageSmokePath()
        {
            ExrHeader header = new ExrHeader
            {
                Compression = CompressionType.None,
            };
            Exr.EXRSetNameAttr(header, "managed-smoke");
            Assert.AreEqual("managed-smoke", header.Name);

            ExrImage image = new ExrImage(
                width: 2,
                height: 1,
                channels: new[]
                {
                    new ExrImageChannel(new ExrChannel("A", ExrPixelType.Float), ExrPixelType.Float, ToBytes(new[] { 1.0f, 1.0f })),
                    new ExrImageChannel(new ExrChannel("B", ExrPixelType.Float), ExrPixelType.Float, ToBytes(new[] { 0.0f, 0.0f })),
                    new ExrImageChannel(new ExrChannel("G", ExrPixelType.Float), ExrPixelType.Float, ToBytes(new[] { 0.5f, 0.5f })),
                    new ExrImageChannel(new ExrChannel("R", ExrPixelType.Float), ExrPixelType.Float, ToBytes(new[] { 1.0f, 0.0f })),
                });

            ResultCode saveResult = Exr.SaveEXRImageToMemory(image, header, out byte[] encoded);
            Assert.AreEqual(ResultCode.Success, saveResult);
            Assert.IsTrue(Exr.IsEXRFromMemory(encoded));

            ResultCode parseResult = Exr.ParseEXRHeaderFromMemory(encoded, out _, out ExrHeader parsedHeader);
            Assert.AreEqual(ResultCode.Success, parseResult);

            ResultCode loadResult = Exr.LoadEXRImageFromMemory(encoded, parsedHeader, out ExrImage decoded);
            Assert.AreEqual(ResultCode.Success, loadResult);
            Assert.AreEqual(1, Exr.EXRNumLevels(decoded));
            Assert.AreEqual(2, decoded.Width);
            Assert.AreEqual(1, decoded.Height);
            Assert.AreEqual(4, decoded.Channels.Count);
        }

        [TestMethod]
        public void ManagedImageLevelsArePopulatedForCurrentlySupportedMultiResolutionSamples()
        {
            string bonita = TestData.Sample(System.IO.Path.Combine("MultiResolution", "Bonita.exr"));
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(bonita, out _, out ExrHeader bonitaHeader));
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(bonita, bonitaHeader, out ExrImage bonitaImage));
            Assert.AreEqual(10, Exr.EXRNumLevels(bonitaImage));
            Assert.AreEqual(10, bonitaImage.Levels.Count);
            Assert.IsTrue(bonitaImage.Levels[0].Tiles.Count > 0);

            string kapaa = TestData.Sample(System.IO.Path.Combine("MultiResolution", "Kapaa.exr"));
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromFile(kapaa, out _, out ExrHeader kapaaHeader));
            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromFile(kapaa, kapaaHeader, out ExrImage kapaaImage));
            Assert.AreEqual(11 * 11, Exr.EXRNumLevels(kapaaImage));
            Assert.AreEqual(11 * 11, kapaaImage.Levels.Count);
            Assert.IsTrue(kapaaImage.Levels[0].Tiles.Count > 0);
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
