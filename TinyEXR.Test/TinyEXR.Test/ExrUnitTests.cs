using System;
using System.Collections.Generic;
using System.IO;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrUnitTests
    {
        [TestMethod]
        public void VersionProbeAndMagicValidationWork()
        {
            byte[] sample = File.ReadAllBytes(TestData.Regression("2by2.exr"));

            ResultCode versionResult = Exr.TryReadVersion(sample, out ExrVersion version);
            Assert.AreEqual(ResultCode.Success, versionResult);
            Assert.AreEqual(2, version.Version);
            Assert.IsFalse(version.Tiled);
            Assert.IsFalse(version.Multipart);
            Assert.IsTrue(Exr.IsExr(sample));
            Assert.IsTrue(Exr.IsExr(TestData.Regression("2by2.exr")));

            byte[] invalidMagic = (byte[])sample.Clone();
            invalidMagic[0] ^= 0xff;
            Assert.AreEqual(ResultCode.InvalidMagicNumver, Exr.TryReadVersion(invalidMagic, out _));
            Assert.IsFalse(Exr.IsExr(invalidMagic));

            byte[] invalidVersion = new byte[8];
            invalidVersion[0] = 0x76;
            invalidVersion[1] = 0x2f;
            invalidVersion[2] = 0x31;
            invalidVersion[3] = 0x01;
            invalidVersion[4] = 0;
            Assert.AreEqual(ResultCode.InvalidExrVersion, Exr.TryReadVersion(invalidVersion, out _));
        }

        [TestMethod]
        public void HeaderMetadataAndMemoryReadWork()
        {
            string samplePath = TestData.Regression("2by2.exr");
            byte[] sample = File.ReadAllBytes(samplePath);

            ResultCode headerResult = Exr.TryReadHeader(sample, out ExrVersion version, out ExrHeader header);
            Assert.AreEqual(ResultCode.Success, headerResult);
            Assert.IsFalse(version.Tiled);
            Assert.AreEqual(CompressionType.None, header.Compression);
            Assert.AreEqual(new ExrBox2i(0, 0, 1, 1), header.DataWindow);
            Assert.AreEqual(new ExrBox2i(0, 0, 1, 1), header.DisplayWindow);
            Assert.AreEqual(4, header.Channels.Count);
            CollectionAssert.AreEqual(
                new[] { "A", "B", "G", "R" },
                GetChannelNames(header.Channels));

            ResultCode loadResult = Exr.LoadEXRFromMemory(sample, out float[] rgba, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, loadResult);
            Assert.AreEqual(2, width);
            Assert.AreEqual(2, height);
            TestHelpers.AssertFloatSequence(
                new[]
                {
                    1.0f, 1.0f, 1.0f, 1.0f,
                    1.0f, 0.0f, 0.0f, 1.0f,
                    0.0f, 0.4470215f, 1.0f, 0.2509766f,
                    0.0f, 0.0f, 0.0f, 1.0f,
                },
                rgba,
                0.0001f);
        }

        [TestMethod]
        public void CustomAttributesAndUnicodePathsAreHandled()
        {
            ResultCode metadataResult = Exr.TryReadHeader(
                TestData.Sample(Path.Combine("Chromaticities", "Rec709.exr")),
                out ExrHeader metadataHeader);
            Assert.AreEqual(ResultCode.Success, metadataResult);
            Assert.AreEqual(1, metadataHeader.CustomAttributes.Count);
            Assert.AreEqual("owner", metadataHeader.CustomAttributes[0].Name);
            Assert.AreEqual(
                "Copyright 2006 Industrial Light & Magic",
                metadataHeader.CustomAttributes[0].GetStringValue());

            ResultCode unicodeLoadResult = Exr.LoadEXR(
                TestData.Regression("日本語.exr"),
                out float[] rgba,
                out int width,
                out int height);
            Assert.AreEqual(ResultCode.Success, unicodeLoadResult);
            Assert.AreEqual(2, width);
            Assert.AreEqual(2, height);
            Assert.AreEqual(16, rgba.Length);
        }

        [TestMethod]
        public void InvalidHeaderAndInvalidDataSamplesReportErrors()
        {
            Assert.AreEqual(
                ResultCode.InvalidHeader,
                Exr.TryReadHeader(TestData.Regression("poc-eedff3a9e99eb1c0fd3a3b0989e7c44c0a69f04f10b23e5264f362a4773f4397_min"), out _));

            Assert.AreEqual(
                ResultCode.InvalidData,
                Exr.TryReadHeader(TestData.Regression("poc-df76d1f27adb8927a1446a603028272140905c168a336128465a1162ec7af270.mini"), out _));
        }

        [TestMethod]
        public void ManagedSpectralHelpersWork()
        {
            Assert.AreEqual("550,000000", Exr.EXRFormatWavelength(550.0f));

            string reflective = Exr.EXRReflectiveChannelName(650.0f);
            string polarised = Exr.EXRSpectralChannelName(550.0f, 2);
            Assert.AreEqual("T.650,000000nm", reflective);
            Assert.AreEqual("S2.550,000000nm", polarised);
            Assert.AreEqual(650.0f, Exr.EXRParseSpectralChannelWavelength(reflective), 0.001f);
            Assert.AreEqual(550.0f, Exr.EXRParseSpectralChannelWavelength(polarised), 0.001f);
            Assert.AreEqual(-1, Exr.EXRGetStokesComponent(reflective));
            Assert.AreEqual(2, Exr.EXRGetStokesComponent(polarised));
            Assert.IsTrue(Exr.EXRIsSpectralChannel(reflective));
            Assert.IsTrue(Exr.EXRIsSpectralChannel(polarised));

            ExrHeader header = new ExrHeader();
            Assert.AreEqual(
                ResultCode.Success,
                Exr.EXRSetSpectralAttributes(header, SpectrumType.Polarised, "W.m^-2.sr^-1.nm^-1"));
            Assert.AreEqual("W.m^-2.sr^-1.nm^-1", Exr.EXRGetSpectralUnits(header));

            header.Channels.Add(new ExrChannel(Exr.EXRSpectralChannelName(650.0f, 0), ExrPixelType.Float));
            header.Channels.Add(new ExrChannel(Exr.EXRSpectralChannelName(550.0f, 1), ExrPixelType.Float));
            header.Channels.Add(new ExrChannel(Exr.EXRSpectralChannelName(550.0f, 0), ExrPixelType.Float));

            Assert.AreEqual(SpectrumType.Polarised, Exr.EXRGetSpectrumType(header));
            float[] wavelengths = Exr.EXRGetWavelengths(header);
            Assert.AreEqual(2, wavelengths.Length);
            Assert.AreEqual(550.0f, wavelengths[0], 0.001f);
            Assert.AreEqual(650.0f, wavelengths[1], 0.001f);
        }

        private static string[] GetChannelNames(IList<ExrChannel> channels)
        {
            string[] names = new string[channels.Count];
            for (int i = 0; i < channels.Count; i++)
            {
                names[i] = channels[i].Name;
            }

            return names;
        }
    }
}
