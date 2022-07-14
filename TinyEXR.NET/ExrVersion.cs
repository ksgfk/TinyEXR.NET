using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExrVersion
    {
        public int Version;
        public int Tiled;
        public int LongName;
        public int NonImage;
        public int Multipart;
    }
}
