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
    }
}
