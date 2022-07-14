using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExrHeader
    {
        public float PixelAspectRatio;
        public int LineOrder;
        public ExrBox2i DataWindow;
        public ExrBox2i DisplayWindow;
        public fixed float ScreenWindowCenter[2];
        public float ScreenWindowWidth;
        public int ChunkCount;

        public int Tiled;
        public int TileSizeX;
        public int TileSizeY;
        public int TileLevelMode;
        public int TileRoundingMode;

        public int LongName;
        public int NonImage;
        public int Multipart;
        public uint HeaderLen;

        public int NumCustomAttributes;
        public ExrAttribute* CustomAttributes;

        public ExrChannelInfo* Channels;

        public int* PixelTypes;

        public int NumChannels;

        public int CompressionType;
        public int* RequestedPixelTypes;

        public fixed byte Name[256];
    }
}
