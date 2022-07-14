using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExrChannelInfo
    {
        public fixed byte Name[256];
        public int PixelType;
        public int Xsampling;
        public int Ysampling;
        public byte Plinear; //unsigned char
        public fixed byte __pad[3];
    }
}
