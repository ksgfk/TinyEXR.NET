using System.Collections.Generic;
using System.Linq;

namespace TinyEXR.Test
{
    [TestClass]
    public sealed class ExrCompressionManifestParityTests
    {
        [TestMethod]
        [DynamicData(nameof(CompressionActiveCases))]
        public void CompressionActiveManifestEntriesExecuteRealParityChecks(string csharpCaseName)
        {
            ExrCompressionParityTests compression = new ExrCompressionParityTests();
            ExrUpstreamCompatibilityTests compatibility = new ExrUpstreamCompatibilityTests();

            switch (csharpCaseName)
            {
                case "Parity_Tiles_GoldenGate_Load":
                    compatibility.CompressionSamplesFormerlyBlockedByCodecGapsNowDecode(System.IO.Path.Combine("Tiles", "GoldenGate.exr"), false);
                    break;
                case "Parity_LuminanceChroma_Garden_Load":
                    compatibility.CompressionSamplesFormerlyBlockedByCodecGapsNowDecode(System.IO.Path.Combine("LuminanceChroma", "Garden.exr"), false);
                    break;
                case "Parity_Regression_Issue194_Piz":
                    compatibility.CompressionSamplesFormerlyBlockedByCodecGapsNowDecode("000-issue194.exr", true);
                    break;
                case "Parity_Regression_Issue160_Piz":
                    compatibility.CompressionSamplesFormerlyBlockedByCodecGapsNowDecode("issue-160-piz-decode.exr", true);
                    break;
                case "Parity_Regression_Issue100":
                    compatibility.CompressionSamplesFormerlyBlockedByCodecGapsNowDecode("piz-bug-issue-100.exr", true);
                    break;
                case "Parity_PIZ_Compression_RoundTrip":
                    compression.Parity_PIZ_Compression_RoundTrip();
                    break;
                case "Parity_PIZ_Compression_AllZeros":
                    compression.Parity_PIZ_Compression_AllZeros();
                    break;
                case "Parity_PIZ_LargeImageCompression":
                    compression.Parity_PIZ_LargeImageCompression();
                    break;
                case "Parity_PXR24_Compression_RoundTrip":
                    compatibility.CompressionSamplesFormerlyBlockedByCodecGapsNowDecode(System.IO.Path.Combine("Tiles", "Spirals.exr"), false);
                    compression.Parity_PXR24_Compression_RoundTrip();
                    break;
                case "Parity_B44_Compression_RoundTrip":
                    compression.Parity_B44_Compression_RoundTrip();
                    break;
                case "Parity_Regression_B44_MixedChannelTypes":
                    compression.Parity_Regression_B44_MixedChannelTypes();
                    break;
                case "Parity_Regression_B44_AllFloatChannels":
                    compression.Parity_Regression_B44_AllFloatChannels();
                    break;
                case "Parity_Regression_B44_UIntHalfMixedChannels":
                    compression.Parity_Regression_B44_UIntHalfMixedChannels();
                    break;
                case "Parity_Regression_B44A_MixedChannelTypes":
                    compression.Parity_Regression_B44A_MixedChannelTypes();
                    break;
                case "Parity_Regression_B44_NonPowerOf2Dimensions":
                    compression.Parity_Regression_B44_NonPowerOf2Dimensions();
                    break;
                case "Parity_B44A_FlatBlockCompression":
                    compression.Parity_B44A_FlatBlockCompression();
                    break;
                default:
                    Assert.Fail($"Compression manifest case '{csharpCaseName}' is not wired to a real parity check.");
                    break;
            }
        }

        public static IEnumerable<object[]> CompressionActiveCases()
        {
            HashSet<string> explicitCompressionLoads = new HashSet<string>(System.StringComparer.Ordinal)
            {
                "Parity_Tiles_GoldenGate_Load",
                "Parity_LuminanceChroma_Garden_Load",
                "Parity_Regression_Issue194_Piz",
                "Parity_Regression_Issue160_Piz",
                "Parity_Regression_Issue100",
            };

            foreach (UpstreamCaseManifestEntry entry in UpstreamCaseManifest.ActiveCases)
            {
                bool taggedCompression =
                    entry.UpstreamTags.Contains("[PIZ]", System.StringComparison.Ordinal) ||
                    entry.UpstreamTags.Contains("[PXR24]", System.StringComparison.Ordinal) ||
                    entry.UpstreamTags.Contains("[B44]", System.StringComparison.Ordinal) ||
                    entry.UpstreamTags.Contains("[B44A]", System.StringComparison.Ordinal);
                if (taggedCompression || explicitCompressionLoads.Contains(entry.CSharpCaseName))
                {
                    yield return new object[] { entry.CSharpCaseName };
                }
            }
        }
    }
}
