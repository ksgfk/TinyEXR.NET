namespace TinyEXR.Format
{
    internal static class FormatCapabilities
    {
        // Official OpenEXR 3.5.3 compression.c / exr_compression_is_valid_for_deep.
        internal static bool IsValidDeepCompression(Compression compression) =>
            compression is Compression.None or Compression.RLE or Compression.ZIPS or Compression.ZSTD;

        internal static bool IsImplemented(Compression compression) =>
            compression >= Compression.None && compression <= Compression.HTJ2K32;

        // OpenEXR MultiPartOutputFile's shared-attribute rule: values present in
        // the first part must also be present and equal in subsequent parts.
        internal static bool HasMatchingSharedAttributes(Header first, Header other) =>
            first.DisplayWindow.Equals(other.DisplayWindow) &&
            first.PixelAspectRatio == other.PixelAspectRatio &&
            (!first.Chromaticities.HasValue || first.Chromaticities.Equals(other.Chromaticities)) &&
            MatchesAttribute(first, other, "timeCode");

        private static bool MatchesAttribute(Header first, Header other, string name)
        {
            foreach (HeaderAttribute attribute in first.Attributes)
            {
                if (attribute.Name != name) continue;
                foreach (HeaderAttribute candidate in other.Attributes)
                    if (candidate.Name == name)
                        return candidate.TypeName == attribute.TypeName && candidate.Data.SequenceEqual(attribute.Data);
                return false;
            }
            return true;
        }

        internal static bool IsValidSampling(Header header, Channel channel) =>
            header.IsTiled || header.IsDeep
                ? channel.XSampling == 1 && channel.YSampling == 1
                : header.DataWindow.MinX % channel.XSampling == 0 &&
                  header.DataWindow.MinY % channel.YSampling == 0 &&
                  header.DataWindow.Width % channel.XSampling == 0 &&
                  header.DataWindow.Height % channel.YSampling == 0;
    }
}
