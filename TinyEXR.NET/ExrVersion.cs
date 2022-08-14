namespace TinyEXR
{
    public struct ExrVersion
    {
        internal Native.EXRVersion _impl;

        public int Version { get => _impl.version; set => _impl.version = value; }
        public bool IsTiled { get => _impl.tiled == 0; set => _impl.tiled = value ? 1 : 0; }
        public bool HasLongName { get => _impl.long_name == 0; set => _impl.long_name = value ? 1 : 0; }
        public bool HasNonImageParts { get => _impl.non_image == 0; set => _impl.non_image = value ? 1 : 0; }
        public bool IsMultipart { get => _impl.multipart == 0; set => _impl.multipart = value ? 1 : 0; }
    }
}
