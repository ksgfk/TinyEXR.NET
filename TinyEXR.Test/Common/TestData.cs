using System;
using System.IO;

namespace TinyEXR.Test
{
    internal static class TestData
    {
        public static string RepositoryRoot { get; } = FindRepositoryRoot();

        public static string OpenExrImagesRoot { get; } = ResolveOpenExrImagesRoot();

        public static string RegressionRoot { get; } = Path.Combine(RepositoryRoot, "TinyEXR.Native", "tinyexr", "test", "unit", "regression");

        public static string NativeRoot { get; } = Path.Combine(RepositoryRoot, "TinyEXR.Native", "tinyexr");

        public static string Sample(string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(OpenExrImagesRoot, relativePath));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"OpenEXR sample file not found: {fullPath}", fullPath);
            }

            return fullPath;
        }

        public static string Regression(string fileName)
        {
            string fullPath = Path.GetFullPath(Path.Combine(RegressionRoot, fileName));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"tinyexr regression file not found: {fullPath}", fullPath);
            }

            return fullPath;
        }

        public static string NativeSample(string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(NativeRoot, relativePath));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"tinyexr native sample file not found: {fullPath}", fullPath);
            }

            return fullPath;
        }

        private static string ResolveOpenExrImagesRoot()
        {
            string? overridden = Environment.GetEnvironmentVariable("TINYEXR_OPENEXR_IMAGES_ROOT");
            string root = string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(RepositoryRoot, ".cache", "openexr-images")
                : Path.GetFullPath(overridden);

            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"OpenEXR sample directory was not found. Expected '{root}'. " +
                    "Set TINYEXR_OPENEXR_IMAGES_ROOT to override the location.");
            }

            return root;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                bool looksLikeRoot =
                    Directory.Exists(Path.Combine(directory.FullName, "TinyEXR.NET")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "TinyEXR.Test")) &&
                    File.Exists(Path.Combine(directory.FullName, "README.md"));
                if (looksLikeRoot)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Unable to locate the TinyEXR.NET repository root from the test output directory.");
        }
    }
}
