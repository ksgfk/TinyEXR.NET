namespace TinyEXR
{
    public sealed class ExrVersion
    {
        public int Version { get; internal set; }

        public bool Tiled { get; internal set; }

        public bool LongName { get; internal set; }

        public bool NonImage { get; internal set; }

        public bool Multipart { get; internal set; }
    }
}
