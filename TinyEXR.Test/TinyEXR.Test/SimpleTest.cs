using System.Runtime.InteropServices;
using System.Text;
using TinyEXR.Native;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class SimpleTest
    {
        [TestMethod]
        public void Load()
        {
            ResultCode rc = Exr.LoadEXR("table_mountain_2_puresky_1k.exr", out float[] rgba, out int width, out int height);
            Assert.AreEqual(ResultCode.Success, rc);
            Assert.IsNotNull(rgba);
            Assert.AreEqual(1024, width);
            Assert.AreEqual(512, height);
            Assert.AreEqual(2097152, rgba.Length);

            Assert.AreEqual(0.06982f, rgba[0], 0.00001f);
            Assert.AreEqual(0.08445f, rgba[1], 0.00001f);
            Assert.AreEqual(0.12244f, rgba[2], 0.00001f);
            Assert.AreEqual(1.0f, rgba[3], 0.00001f);

            Assert.AreEqual(0.06972f, rgba[4], 0.00001f);
            Assert.AreEqual(0.08442f, rgba[5], 0.00001f);
            Assert.AreEqual(0.12234f, rgba[6], 0.00001f);
            Assert.AreEqual(1.0f, rgba[7], 0.00001f);
        }

        [TestMethod]
        public void Save()
        {
            int width = 256;
            int height = 512;
            int blockSize = 16;
            int components = 4;
            float[] rgba = new float[width * height * components];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int blockX = x / blockSize;
                    int blockY = y / blockSize;
                    bool isWhite = (blockX + blockY) % 2 == 0;
                    float value = isWhite ? 1.0f : 0.0f;
                    int idx = (y * width + x) * components;
                    rgba[idx + 0] = value;
                    rgba[idx + 1] = value;
                    rgba[idx + 2] = value;
                    rgba[idx + 3] = 1.0f;
                }
            }

            ResultCode rc = Exr.SaveEXRToMemory(rgba, width, height, components, false, out byte[] result);
            Assert.AreEqual(ResultCode.Success, rc);
            Assert.AreEqual(11243, result.Length);
        }

        [TestMethod]
        public unsafe void SpectralHelpers()
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

            EXRHeader header = default;
            try
            {
                Exr.InitEXRHeader(ref header);

                ResultCode rc = Exr.EXRSetSpectralAttributes(ref header, SpectrumType.Polarised, "W.m^-2.sr^-1.nm^-1");
                Assert.AreEqual(ResultCode.Success, rc);
                Assert.AreEqual("W.m^-2.sr^-1.nm^-1", Exr.EXRGetSpectralUnits(ref header));

                EXRChannelInfo[] channels = new EXRChannelInfo[3];
                SetChannelName(ref channels[0], Exr.EXRSpectralChannelName(650.0f, 0));
                SetChannelName(ref channels[1], Exr.EXRSpectralChannelName(550.0f, 1));
                SetChannelName(ref channels[2], Exr.EXRSpectralChannelName(550.0f, 0));

                fixed (EXRChannelInfo* channelsPtr = channels)
                {
                    header.channels = channelsPtr;
                    header.num_channels = channels.Length;

                    Assert.AreEqual(SpectrumType.Polarised, Exr.EXRGetSpectrumType(ref header));

                    float[] wavelengths = Exr.EXRGetWavelengths(ref header);
                    Assert.AreEqual(2, wavelengths.Length);
                    Assert.AreEqual(550.0f, wavelengths[0], 0.001f);
                    Assert.AreEqual(650.0f, wavelengths[1], 0.001f);

                    header.channels = null;
                    header.num_channels = 0;
                }
            }
            finally
            {
                Exr.FreeEXRHeader(ref header);
            }
        }

        [TestMethod]
        public void WriterSupportsPXR24Compression()
        {
            const int width = 8;
            const int height = 4;
            const int pixelCount = width * height;

            float[] r = new float[pixelCount];
            float[] g = new float[pixelCount];
            float[] b = new float[pixelCount];
            float[] a = new float[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                r[i] = i / (float)pixelCount;
                g[i] = 1.0f - r[i];
                b[i] = (i % width) / (float)width;
                a[i] = 1.0f;
            }

            ScanlineExrWriter writer = new ScanlineExrWriter()
                .SetSize(width, height)
                .SetCompression(CompressionType.PXR24)
                .AddChannel("R", ExrPixelType.Float, ToBytes(r), ExrPixelType.Float)
                .AddChannel("G", ExrPixelType.Float, ToBytes(g), ExrPixelType.Float)
                .AddChannel("B", ExrPixelType.Float, ToBytes(b), ExrPixelType.Float)
                .AddChannel("A", ExrPixelType.Float, ToBytes(a), ExrPixelType.Float);

            byte[] result = writer.Save()!;

            ResultCode versionRc = Exr.ParseEXRVersionFromMemory(result, out EXRVersion version);
            Assert.AreEqual(ResultCode.Success, versionRc);

            EXRHeader header = default;
            try
            {
                Exr.InitEXRHeader(ref header);
                ResultCode headerRc = Exr.ParseEXRHeaderFromMemory(result, ref version, ref header);
                Assert.AreEqual(ResultCode.Success, headerRc);
                Assert.AreEqual((int)CompressionType.PXR24, header.compression_type);
            }
            finally
            {
                Exr.FreeEXRHeader(ref header);
            }
        }

        private static byte[] ToBytes(float[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(float)];
            MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(bytes);
            return bytes;
        }

        private static unsafe void SetChannelName(ref EXRChannelInfo channel, string name)
        {
            fixed (sbyte* namePtr = channel.name)
            {
                Span<byte> buffer = new Span<byte>(namePtr, 256);
                buffer.Clear();

                int byteCount = Encoding.UTF8.GetByteCount(name);
                Assert.IsTrue(byteCount < buffer.Length);
                Encoding.UTF8.GetBytes(name, buffer[..byteCount]);
            }
        }
    }
}
