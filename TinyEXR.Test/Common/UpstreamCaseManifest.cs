using System.Collections.Generic;

namespace TinyEXR.Test
{
    internal sealed record UpstreamCaseManifestEntry(
        string UpstreamCaseName,
        string UpstreamTags,
        int SourceLine,
        string CSharpCaseName,
        string ApplicableTfm,
        bool IsFeatureCompletion);

    internal static class UpstreamCaseManifest
    {
        public static IReadOnlyList<UpstreamCaseManifestEntry> ActiveCases { get; } =
            new[]
            {
                Entry("asakusa", "[Load]", 187, "Parity_asakusa"),
                Entry("utf8filename", "[Load]", 206, "Parity_utf8filename"),
                Entry("ScanLines", "[Load]", 278, "Parity_ScanLines"),
                Entry("Chromaticities", "[Load]", 318, "Parity_Chromaticities"),
                Entry("TestImages", "[Load]", 353, "Parity_TestImages"),
                Entry("LuminanceChroma", "[Load]", 395, "Parity_LuminanceChroma"),
                Entry("DisplayWindow", "[Load]", 431, "Parity_DisplayWindow"),
                Entry("Tiles/GoldenGate.exr", "[Version]", 478, "Parity_Tiles_GoldenGate_Version"),
                Entry("Tiles/GoldenGate.exr|Load", "[Load]", 486, "Parity_Tiles_GoldenGate_Load"),
                Entry("LuminanceChroma/Garden.exr|Load", "[Load]", 520, "Parity_LuminanceChroma_Garden_Load"),
                Entry("Tiles/Ocean.exr", "[Load]", 546, "Parity_Tiles_Ocean_Load"),
                Entry("MultiResolution/Bonita.exr", "[Load]", 572, "Parity_MultiResolution_Bonita_Load"),
                Entry("MultiResolution/Kapaa.exr", "[Load]", 603, "Parity_MultiResolution_Kapaa_Load"),
                Entry("Saving ScanLines", "[Save]", 636, "Parity_Saving_ScanLines"),
                Entry("Saving MultiResolution", "[Save]", 718, "Parity_Saving_MultiResolution"),
                Entry("Saving multipart", "[Save]", 794, "Parity_Saving_Multipart"),
                Entry("Saving multipart|Combine", "[Save]", 900, "Parity_Saving_Multipart_Combine"),
                Entry("Beachball/multipart.0001.exr", "[Version]", 1040, "Parity_Beachball_Multipart_Version"),
                Entry("Beachball/multipart.0001.exr|Load", "[Load]", 1050, "Parity_Beachball_Multipart_Load"),
                Entry("Beachbal multiparts", "[Load]", 1088, "Parity_Beachball_Multiparts"),
                Entry("Beachbal singleparts", "[Load]", 1137, "Parity_Beachball_Singleparts"),
                Entry("ParseEXRVersionFromMemory invalid input", "[Parse]", 1178, "Parity_ParseEXRVersionFromMemory_InvalidInput"),
                Entry("ParseEXRHeaderFromMemory invalid input", "[Parse]", 1215, "Parity_ParseEXRHeaderFromMemory_InvalidInput"),
                Entry("Compressed is smaller than uncompressed", "[Issue40]", 1258, "Parity_CompressedIsSmallerThanUncompressed"),
                Entry("Regression: Issue50", "[fuzzing]", 1343, "Parity_Regression_Issue50"),
                Entry("Regression: Issue57", "[fuzzing]", 1372, "Parity_Regression_Issue57"),
                Entry("Regression: Issue56", "[fuzzing]", 1401, "Parity_Regression_Issue56"),
                Entry("Regression: Issue61", "[fuzzing]", 1429, "Parity_Regression_Issue61"),
                Entry("Regression: Issue60", "[fuzzing]", 1458, "Parity_Regression_Issue60"),
                Entry("Regression: Issue71", "[issue71]", 1487, "Parity_Regression_Issue71"),
                Entry("Regression: Issue93", "[issue93]", 1511, "Parity_Regression_Issue93"),
                Entry("Regression: Issue100", "[issue100]", 1548, "Parity_Regression_Issue100"),
                Entry("Regression: Issue53|Channels", "[issue53]", 1601, "Parity_Regression_Issue53_Channels"),
                Entry("Regression: Issue53|Image", "[issue53]", 1639, "Parity_Regression_Issue53_Image"),
                Entry("Regression: Issue53|Image|Missing Layer", "[issue53]", 1677, "Parity_Regression_Issue53_Image_MissingLayer"),
                Entry("Regression: PR150|Read|1x1 1xhalf", "[pr150]", 1694, "Parity_Regression_PR150_Read_1x1_1xhalf"),
                Entry("Regression: Issue194|Piz", "[issue194]", 1722, "Parity_Regression_Issue194_Piz"),
                Entry("Regression: Issue238|DoubleFree", "[issue238]", 1747, "Parity_Regression_Issue238_DoubleFree"),
                Entry("Regression: Issue238|DoubleFree|Multipart", "[issue238]", 1815, "Parity_Regression_Issue238_DoubleFree_Multipart"),
                Entry("Regression: Issue160|Piz", "[issue160]", 1875, "Parity_Regression_Issue160_Piz"),
                Entry("Spectral: Channel naming", "[spectral]", 1919, "Parity_Spectral_ChannelNaming"),
                Entry("Spectral: Header attributes", "[spectral]", 1962, "Parity_Spectral_HeaderAttributes"),
                Entry("Spectral: Spectrum type detection", "[spectral]", 1996, "Parity_Spectral_SpectrumTypeDetection"),
                Entry("Spectral: Reflective spectrum type", "[spectral]", 2029, "Parity_Spectral_ReflectiveSpectrumType"),
                Entry("Spectral: Polarised spectrum type", "[spectral]", 2053, "Parity_Spectral_PolarisedSpectrumType"),
                Entry("PIZ: Compression round-trip", "[PIZ]", 2092, "Parity_PIZ_Compression_RoundTrip"),
                Entry("PIZ: Compression all zeros (issue 194)", "[PIZ]", 2249, "Parity_PIZ_Compression_AllZeros"),
                Entry("PIZ: Large image compression", "[PIZ]", 2356, "Parity_PIZ_LargeImageCompression"),
                Entry("PXR24: Compression round-trip", "[PXR24]", 2500, "Parity_PXR24_Compression_RoundTrip"),
                Entry("B44: Compression round-trip", "[B44]", 2616, "Parity_B44_Compression_RoundTrip"),
                Entry("Regression: B44 mixed channel types (issue 239)", "[B44][issue239]", 2723, "Parity_Regression_B44_MixedChannelTypes"),
                Entry("Regression: B44 all-FLOAT channels (issue 239 variant)", "[B44][issue239]", 2850, "Parity_Regression_B44_AllFloatChannels"),
                Entry("Regression: B44 UINT+HALF mixed channels (issue 239 variant)", "[B44][issue239]", 2957, "Parity_Regression_B44_UIntHalfMixedChannels"),
                Entry("Regression: B44A mixed channel types (issue 239 variant)", "[B44A][issue239]", 3063, "Parity_Regression_B44A_MixedChannelTypes"),
                Entry("Regression: B44 non-power-of-2 dimensions (issue 239)", "[B44][issue239]", 3162, "Parity_Regression_B44_NonPowerOf2Dimensions"),
                Entry("B44A: Flat block compression", "[B44A]", 3275, "Parity_B44A_FlatBlockCompression"),
            };

        public static IReadOnlyList<UpstreamCaseManifestEntry> FeatureCompletionCases { get; } =
            new[]
            {
                Feature("Tiles/Spirals.exr", "[Load]", 1012, "Feature_Tiles_Spirals_Load"),
                Feature("Regression: Issue54", "[fuzzing]", 1316, "Feature_Regression_Issue54"),
                Feature("ScanLines/Cannon.exr", "[Load]", 282, "Feature_ScanLines_Cannon"),
                Feature("TestImages/GammaChart.exr", "[Load]", 357, "Feature_TestImages_GammaChart"),
                Feature("TestImages/GrayRampsDiagonal.exr", "[Load]", 359, "Feature_TestImages_GrayRampsDiagonal"),
                Feature("TestImages/GrayRampsHorizontal.exr", "[Load]", 360, "Feature_TestImages_GrayRampsHorizontal"),
                Feature("TestImages/RgbRampsDiagonal.exr", "[Load]", 361, "Feature_TestImages_RgbRampsDiagonal"),
                Feature("TestImages/SquaresSwirls.exr", "[Load]", 362, "Feature_TestImages_SquaresSwirls"),
                Feature("TestImages/WideFloatRange.exr", "[Load]", 364, "Feature_TestImages_WideFloatRange"),
                Feature("LuminanceChroma/CrissyField.exr", "[Load]", 398, "Feature_LuminanceChroma_CrissyField"),
                Feature("LuminanceChroma/Flowers.exr", "[Load]", 399, "Feature_LuminanceChroma_Flowers"),
            };

        private static UpstreamCaseManifestEntry Entry(string upstreamCaseName, string upstreamTags, int sourceLine, string csharpCaseName)
        {
            return new UpstreamCaseManifestEntry(upstreamCaseName, upstreamTags, sourceLine, csharpCaseName, "net10.0", false);
        }

        private static UpstreamCaseManifestEntry Feature(string upstreamCaseName, string upstreamTags, int sourceLine, string csharpCaseName)
        {
            return new UpstreamCaseManifestEntry(upstreamCaseName, upstreamTags, sourceLine, csharpCaseName, "net10.0", true);
        }
    }
}
