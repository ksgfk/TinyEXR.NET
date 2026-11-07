namespace TinyEXR.Format
{
    /// <summary>
    /// OpenEXR version-field state needed by the format and payload layers.
    /// </summary>
    internal readonly struct ParsedFileFlags
    {
        public ParsedFileFlags(bool tiled, bool longNames, bool nonImage, bool multipart)
        {
            Tiled = tiled;
            LongNames = longNames;
            NonImage = nonImage;
            Multipart = multipart;
        }

        public bool Tiled { get; }

        public bool LongNames { get; }

        public bool NonImage { get; }

        public bool Multipart { get; }
    }

}
