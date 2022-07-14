using System;
using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ExrAttribute
    {
        public fixed byte Name[256];
        public fixed byte Type[256];
        public IntPtr Value;  // uint8_t*
        public int Size;
        public int __pad0;
    }
}
