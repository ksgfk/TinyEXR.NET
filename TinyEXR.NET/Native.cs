using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TinyEXR
{
    public enum CallResult
    {
        Success = 0,
        InvalidMagicNumver = -1,
        InvalidExrVersion = -2,
        InvalidArgument = -3,
        InvalidData = -4,
        InvalidFile = -5,
        InvalidParameter = -6,
        CannotOpenFile = -7,
        UnsupportedFormat = -8,
        InvalidHeader = -9,
        UnsupportedFeature = -10,
        CannotWriteFile = -11,
        SerialzationFailed = -12,
        LayerNotFound = -13,
        DataTooLarge = -14
    }

    public unsafe static class Native
    {
        public const string LibraryName = "TinyEXR.NET.Native";

        [DllImport(LibraryName)]
        private static extern int LoadEXR_Export(float** out_rgba, int* width, int* height, byte[] filename, char** err);

        [DllImport(LibraryName)]
        private static extern void FreeEXRErrorMessage_Export(char* msg);

        [DllImport(LibraryName)]
        private static extern void FreeImageData(float* rgba);

        private static long StrLen(char* str)
        {
            char* end = str;
            for (; *end != '\0'; ++end) ;
            return end - str;
        }

        public static CallResult LoadExr(string filename, out float[] rgba, out int width, out int height, out string errorMsg)
        {
            byte[] str = Encoding.UTF8.GetBytes(filename);
            float* img = null;
            int x = 0;
            int y = 0;
            char* errMsg = null;
            CallResult result = (CallResult)LoadEXR_Export(&img, &x, &y, str, &errMsg);
            if (result == CallResult.Success)
            {
                Span<float> imgRef = new Span<float>(img, x * y);
                float[] data = imgRef.ToArray();
                FreeImageData(img);
                width = x;
                height = y;
                rgba = data;
                errorMsg = string.Empty;
            }
            else
            {
                width = default;
                height = default;
                rgba = default;
                if (errMsg == null)
                {
                    errorMsg = string.Empty;
                }
                else
                {
                    string errMsgStr = Encoding.UTF8.GetString((byte*)errMsg, (int)StrLen(errMsg));
                    FreeEXRErrorMessage_Export(errMsg);
                    errorMsg = errMsgStr;
                }
            }
            return result;
        }

        public static CallResult LoadExr(string filename, out float[] rgba, out int width, out int height)
        {
            return LoadExr(filename, out rgba, out width, out height, out _);
        }
    }
}
