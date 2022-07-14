using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DeepImage
    {
        public char** ChannelNames;
        public float*** Image;
        public int** OffsetTable;
        public int NumChannels;
        public int Width;
        public int Height;
        public int __pad0;
    }
}
