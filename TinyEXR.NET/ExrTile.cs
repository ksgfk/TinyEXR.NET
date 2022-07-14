using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExrTile
    {
        public int OffsetX;
        public int OffsetY;
        public int LevelX;
        public int LevelY;

        public int Width;
        public int Height;

        public byte** Images;
    }
}
