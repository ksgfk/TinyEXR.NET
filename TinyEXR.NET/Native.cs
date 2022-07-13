using System.Runtime.InteropServices;

namespace TinyEXR
{
    public unsafe static class Native
    {
        public const string LibraryName = "TinyEXR.NET.Native";

        [DllImport(LibraryName, CharSet = CharSet.Unicode)]
        private static extern int LoadEXR_Export(float** out_rgba, int* width, int* height, char* filename, char** err);
    }
}
