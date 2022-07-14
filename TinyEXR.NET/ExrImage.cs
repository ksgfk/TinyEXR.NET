using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExrImage
    {
        public ExrTile* Tiles;
        public ExrImage* NextLevel;
        public int LevelX;
        public int LevelY;
        public byte** images;
        public int Width;
        public int Height;
        public int NumChannels;
        public int NumTiles;
    }
}
