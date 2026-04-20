using System;
using System.Buffers.Binary;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrCompressionParityTests
    {
        [TestMethod]
        public void RleCompressionRoundTrip()
        {
            const int width = 32;
            const int height = 16;

            float[] r = new float[width * height];
            float[] g = new float[width * height];
            float[] b = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    r[index] = x / (float)(width - 1);
                    g[index] = y / (float)(height - 1);
                    b[index] = ((x / 4) & 1) == 0 ? 0.25f : 0.75f;
                }
            }

            ExrImage image = CreateImage(
                width,
                height,
                new ChannelSpec("B", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(b)),
                new ChannelSpec("G", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(g)),
                new ChannelSpec("R", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(r)));

            ExrImage decoded = RoundTrip(image, CompressionType.RLE, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "B", b, 0.00001f);
            AssertFloatChannel(decoded, "G", g, 0.00001f);
            AssertFloatChannel(decoded, "R", r, 0.00001f);
        }

        [TestMethod]
        public void Parity_PIZ_Compression_RoundTrip()
        {
            const int width = 64;
            const int height = 64;

            float[] r = new float[width * height];
            float[] g = new float[width * height];
            float[] b = new float[width * height];
            float[] a = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    r[index] = x / (float)width;
                    g[index] = y / (float)height;
                    b[index] = 0.5f + 0.1f * MathF.Sin(x * 0.1f);
                    a[index] = 1.0f;
                }
            }

            ExrImage image = CreateHalfRgbaImage(width, height, a, b, g, r);
            ExrImage decoded = RoundTrip(image, CompressionType.PIZ, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", a, 0.01f);
            AssertFloatChannel(decoded, "B", b, 0.01f);
            AssertFloatChannel(decoded, "G", g, 0.01f);
            AssertFloatChannel(decoded, "R", r, 0.01f);
        }

        [TestMethod]
        public void Parity_PIZ_Compression_AllZeros()
        {
            const int width = 16;
            const int height = 16;
            float[] zeros = new float[width * height];

            ExrImage image = CreateHalfRgbaImage(width, height, zeros, zeros, zeros, zeros);
            ExrImage decoded = RoundTrip(image, CompressionType.PIZ, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", zeros, 0.0f);
            AssertFloatChannel(decoded, "B", zeros, 0.0f);
            AssertFloatChannel(decoded, "G", zeros, 0.0f);
            AssertFloatChannel(decoded, "R", zeros, 0.0f);
        }

        [TestMethod]
        public void Parity_PIZ_LargeImageCompression()
        {
            const int width = 256;
            const int height = 256;

            float[] r = new float[width * height];
            float[] g = new float[width * height];
            float[] b = new float[width * height];
            float[] a = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    r[index] = x / (float)width;
                    g[index] = y / (float)height;
                    b[index] = MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f) * 0.5f + 0.5f;
                    a[index] = 1.0f;
                }
            }

            ExrImage image = CreateHalfRgbaImage(width, height, a, b, g, r);
            ExrImage decoded = RoundTrip(image, CompressionType.PIZ, static _ => ExrPixelType.Float, out byte[] encoded, out _);
            Assert.IsTrue(encoded.Length > 0);
            AssertFloatChannel(decoded, "A", a, 0.01f);
            AssertFloatChannel(decoded, "B", b, 0.01f);
            AssertFloatChannel(decoded, "G", g, 0.01f);
            AssertFloatChannel(decoded, "R", r, 0.01f);
        }

        [TestMethod]
        public void Parity_PXR24_Compression_RoundTrip()
        {
            const int width = 32;
            const int height = 32;

            float[] r = new float[width * height];
            float[] g = new float[width * height];
            float[] b = new float[width * height];
            float[] a = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    r[index] = x / (float)width;
                    g[index] = y / (float)height;
                    b[index] = 0.5f;
                    a[index] = 1.0f;
                }
            }

            ExrImage image = CreateHalfRgbaImage(width, height, a, b, g, r);
            ExrImage decoded = RoundTrip(image, CompressionType.PXR24, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", a, 0.01f);
            AssertFloatChannel(decoded, "B", b, 0.01f);
            AssertFloatChannel(decoded, "G", g, 0.01f);
            AssertFloatChannel(decoded, "R", r, 0.01f);
        }

        [TestMethod]
        public void Parity_B44_Compression_RoundTrip()
        {
            const int width = 32;
            const int height = 32;

            float[] r = new float[width * height];
            float[] g = new float[width * height];
            float[] b = new float[width * height];
            float[] a = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    r[index] = x / (float)width;
                    g[index] = y / (float)height;
                    b[index] = 0.5f;
                    a[index] = 1.0f;
                }
            }

            ExrImage image = CreateHalfRgbaImage(width, height, a, b, g, r);
            ExrImage decoded = RoundTrip(image, CompressionType.B44, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", a, 0.02f);
            AssertFloatChannel(decoded, "B", b, 0.02f);
            AssertFloatChannel(decoded, "G", g, 0.02f);
            AssertFloatChannel(decoded, "R", r, 0.02f);
        }

        [TestMethod]
        public void Parity_Regression_B44_MixedChannelTypes()
        {
            const int width = 32;
            const int height = 32;

            float[] a = Fill(width, height, 1.0f);
            float[] g = Fill(width, height, 0.25f);
            float[] r = Fill(width, height, 0.5f);

            ExrImage image = CreateImage(
                width,
                height,
                new ChannelSpec("A", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(a)),
                new ChannelSpec("G", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(g)),
                new ChannelSpec("R", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(r)));

            ExrImage decoded = RoundTrip(
                image,
                CompressionType.B44,
                static channel => channel.Name == "A" ? ExrPixelType.Float : ExrPixelType.Float,
                out _,
                out _);

            AssertFloatChannel(decoded, "A", a, 0.00001f);
            AssertFloatChannel(decoded, "G", g, 0.02f);
            AssertFloatChannel(decoded, "R", r, 0.02f);
        }

        [TestMethod]
        public void Parity_Regression_B44_AllFloatChannels()
        {
            const int width = 32;
            const int height = 32;

            float[] b = new float[width * height];
            float[] g = new float[width * height];
            float[] r = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    b[index] = x / (float)width;
                    g[index] = y / (float)height;
                    r[index] = 0.75f;
                }
            }

            ExrImage image = CreateImage(
                width,
                height,
                new ChannelSpec("B", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(b)),
                new ChannelSpec("G", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(g)),
                new ChannelSpec("R", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(r)));

            ExrImage decoded = RoundTrip(image, CompressionType.B44, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "B", b, 0.0f);
            AssertFloatChannel(decoded, "G", g, 0.0f);
            AssertFloatChannel(decoded, "R", r, 0.0f);
        }

        [TestMethod]
        public void Parity_Regression_B44_UIntHalfMixedChannels()
        {
            const int width = 16;
            const int height = 16;

            uint[] a = new uint[width * height];
            float[] b = Fill(width, height, 0.5f);
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = (uint)i;
            }

            ExrImage image = CreateImage(
                width,
                height,
                new ChannelSpec("A", ExrPixelType.UInt, ExrPixelType.UInt, 0, ToUIntBytes(a)),
                new ChannelSpec("B", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(b)));

            ExrImage decoded = RoundTrip(
                image,
                CompressionType.B44,
                static channel => channel.Type == ExrPixelType.UInt ? ExrPixelType.UInt : ExrPixelType.Float,
                out _,
                out _);

            AssertUIntChannel(decoded, "A", a);
            AssertFloatChannel(decoded, "B", b, 0.02f);
        }

        [TestMethod]
        public void Parity_Regression_B44A_MixedChannelTypes()
        {
            const int width = 32;
            const int height = 32;

            float[] a = Fill(width, height, 1.0f);
            float[] g = Fill(width, height, 0.25f);
            float[] r = Fill(width, height, 0.5f);

            ExrImage image = CreateImage(
                width,
                height,
                new ChannelSpec("A", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(a)),
                new ChannelSpec("G", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(g)),
                new ChannelSpec("R", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(r)));

            ExrImage decoded = RoundTrip(image, CompressionType.B44A, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", a, 0.00001f);
            AssertFloatChannel(decoded, "G", g, 0.02f);
            AssertFloatChannel(decoded, "R", r, 0.02f);
        }

        [TestMethod]
        public void Parity_Regression_B44_NonPowerOf2Dimensions()
        {
            const int width = 13;
            const int height = 7;

            float[] a = new float[width * height];
            float[] g = Fill(width, height, 0.25f);
            float[] r = Fill(width, height, 0.5f);
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = i * 0.01f;
            }

            ExrImage image = CreateImage(
                width,
                height,
                new ChannelSpec("A", ExrPixelType.Float, ExrPixelType.Float, 0, ToFloatBytes(a)),
                new ChannelSpec("G", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(g)),
                new ChannelSpec("R", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(r)));

            ExrImage decoded = RoundTrip(image, CompressionType.B44, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", a, 0.00001f);
            AssertFloatChannel(decoded, "G", g, 0.02f);
            AssertFloatChannel(decoded, "R", r, 0.02f);
        }

        [TestMethod]
        public void Parity_B44A_FlatBlockCompression()
        {
            const int width = 16;
            const int height = 16;

            float[] a = Fill(width, height, 1.0f);
            float[] b = Fill(width, height, 0.0f);
            float[] g = Fill(width, height, 0.5f);
            float[] r = Fill(width, height, 0.25f);

            ExrImage image = CreateHalfRgbaImage(width, height, a, b, g, r);
            ExrImage decoded = RoundTrip(image, CompressionType.B44A, static _ => ExrPixelType.Float, out _, out _);
            AssertFloatChannel(decoded, "A", a, 0.02f);
            AssertFloatChannel(decoded, "B", b, 0.02f);
            AssertFloatChannel(decoded, "G", g, 0.02f);
            AssertFloatChannel(decoded, "R", r, 0.02f);
        }

        private static ExrImage CreateHalfRgbaImage(int width, int height, float[] a, float[] b, float[] g, float[] r)
        {
            return CreateImage(
                width,
                height,
                new ChannelSpec("A", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(a)),
                new ChannelSpec("B", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(b)),
                new ChannelSpec("G", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(g)),
                new ChannelSpec("R", ExrPixelType.Half, ExrPixelType.Float, 0, ToFloatBytes(r)));
        }

        private static ExrImage CreateImage(int width, int height, params ChannelSpec[] specs)
        {
            ExrImageChannel[] channels = new ExrImageChannel[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                ChannelSpec spec = specs[i];
                channels[i] = new ExrImageChannel(
                    new ExrChannel(spec.Name, spec.FileType, spec.FileType, 1, 1, spec.Linear),
                    spec.DataType,
                    spec.Data);
            }

            return new ExrImage(width, height, channels);
        }

        private static ExrImage RoundTrip(
            ExrImage image,
            CompressionType compression,
            Func<ExrChannel, ExrPixelType> requestedTypeSelector,
            out byte[] encoded,
            out ExrHeader parsedHeader)
        {
            ExrHeader header = new ExrHeader
            {
                Compression = compression,
            };

            Assert.AreEqual(ResultCode.Success, Exr.SaveEXRImageToMemory(image, header, out encoded));
            Assert.AreEqual(ResultCode.Success, Exr.ParseEXRHeaderFromMemory(encoded, out _, out parsedHeader));
            for (int i = 0; i < parsedHeader.Channels.Count; i++)
            {
                parsedHeader.Channels[i].RequestedPixelType = requestedTypeSelector(parsedHeader.Channels[i]);
            }

            Assert.AreEqual(ResultCode.Success, Exr.LoadEXRImageFromMemory(encoded, parsedHeader, out ExrImage decoded));
            return decoded;
        }

        private static void AssertFloatChannel(ExrImage image, string channelName, float[] expected, float delta)
        {
            ExrImageChannel channel = FindChannel(image, channelName);
            float[] actual = FromFloatBytes(channel.Data);
            TestHelpers.AssertFloatSequence(expected, actual, delta);
        }

        private static void AssertUIntChannel(ExrImage image, string channelName, uint[] expected)
        {
            ExrImageChannel channel = FindChannel(image, channelName);
            uint[] actual = FromUIntBytes(channel.Data);
            CollectionAssert.AreEqual(expected, actual);
        }

        private static ExrImageChannel FindChannel(ExrImage image, string name)
        {
            foreach (ExrImageChannel channel in image.Channels)
            {
                if (string.Equals(channel.Channel.Name, name, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            throw new AssertFailedException($"Channel '{name}' was not found.");
        }

        private static float[] Fill(int width, int height, float value)
        {
            float[] data = new float[width * height];
            Array.Fill(data, value);
            return data;
        }

        private static byte[] ToFloatBytes(float[] values)
        {
            byte[] data = new byte[values.Length * sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(values[i]));
            }

            return data;
        }

        private static byte[] ToUIntBytes(uint[] values)
        {
            byte[] data = new byte[values.Length * sizeof(uint)];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * sizeof(uint), sizeof(uint)), values[i]);
            }

            return data;
        }

        private static float[] FromFloatBytes(byte[] bytes)
        {
            float[] values = new float[bytes.Length / sizeof(float)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float))));
            }

            return values;
        }

        private static uint[] FromUIntBytes(byte[] bytes)
        {
            uint[] values = new uint[bytes.Length / sizeof(uint)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)));
            }

            return values;
        }

        private readonly struct ChannelSpec
        {
            public ChannelSpec(string name, ExrPixelType fileType, ExrPixelType dataType, byte linear, byte[] data)
            {
                Name = name;
                FileType = fileType;
                DataType = dataType;
                Linear = linear;
                Data = data;
            }

            public string Name { get; }

            public ExrPixelType FileType { get; }

            public ExrPixelType DataType { get; }

            public byte Linear { get; }

            public byte[] Data { get; }
        }
    }
}
