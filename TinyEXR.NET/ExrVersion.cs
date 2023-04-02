using TinyEXR.Native;

namespace TinyEXR
{
    /// <summary>
    /// https://openexr.com/en/latest/OpenEXRFileLayout.html#version-field
    /// </summary>
    public struct ExrVersion
    {
        internal EXRVersion _version;

        public int Version => _version.version;
        public int Tiled => _version.tiled;
        public int LongName => _version.long_name;
        public int NonImage => _version.non_image;
        public int Multipart => _version.multipart;
    }
}
