using System;
using System.IO;

namespace TinyEXR.Test
{
    internal static class TestHelpers
    {
        public static void AssertFloatSequence(float[] expected, float[] actual, float delta)
        {
            Assert.AreEqual(expected.Length, actual.Length, "Float sequence length mismatch.");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], actual[i], delta, $"Float value mismatch at index {i}.");
            }
        }
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TinyEXR.NET.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
