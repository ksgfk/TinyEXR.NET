using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ExrBox2i
    {
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
    }
}
