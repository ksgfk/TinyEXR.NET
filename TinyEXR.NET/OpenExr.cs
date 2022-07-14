using System;
using System.Text;

namespace TinyEXR.NET
{
    public static class OpenExr
    {
        public static CallResult Load(string filename, out float[] rgba, out int width, out int height, out string error)
        {
            return LoadExr(filename, out rgba, out width, out height, out error);
        }

        public static CallResult Load(string filename, out float[] rgba, out int width, out int height)
        {
            return LoadExr(filename, out rgba, out width, out height, out _);
        }

        public static CallResult Load(string filename, string? layer, out float[] rgba, out int width, out int height, out string error)
        {
            return LoadExrWithLayer(filename, layer, out rgba, out width, out height, out error);
        }

        public static CallResult Load(string filename, string? layer, out float[] rgba, out int width, out int height)
        {
            return LoadExrWithLayer(filename, layer, out rgba, out width, out height, out _);
        }

        private unsafe static long StrLen(byte* ptr)
        {
            byte* p = ptr;
            for (; *p != '\0'; p++) ;
            return p - ptr;
        }

        private unsafe static CallResult LoadExr(string filename, out float[] rgba, out int width, out int height, out string error)
        {
            int fileNameByteLength = Encoding.ASCII.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            float* img = null;
            int x = 0;
            int y = 0;
            CallResult result;
            byte* errorPtr = null;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (CallResult)Native.LoadEXR_Export(&img, &x, &y, fileNamePtr, &errorPtr);
            }

            try
            {

                if (result == CallResult.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y);
                    float[] data = imgRef.ToArray();
                    width = x;
                    height = y;
                    rgba = data;
                    error = string.Empty;
                }
                else
                {
                    width = default;
                    height = default;
                    rgba = default!;
                    error = Encoding.UTF8.GetString(errorPtr, (int)StrLen(errorPtr));
                }
            }
            catch (Exception e)
            {
                throw new LoadExrException("managed part exception", e);
            }
            finally
            {
                if (img != null)
                {
                    Native.FreeImageData(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    Native.FreeEXRErrorMessage_Export(errorPtr);
                    errorPtr = null;
                }
            }

            return result;
        }

        private static unsafe CallResult LoadExrWithLayer(string filename, string? layer, out float[] rgba, out int width, out int height, out string error)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            bool hasLayer = !string.IsNullOrWhiteSpace(layer);
            int layerByteLength = hasLayer ? Encoding.UTF8.GetByteCount(layer) : 0;
            Span<byte> layerBytes = hasLayer
                ? (layerByteLength <= 256
                    ? stackalloc byte[256]
                    : new byte[layerByteLength])
                : new Span<byte>();
            if (hasLayer)
            {
                Encoding.UTF8.GetBytes(layer, layerBytes);
            }

            float* img = null;
            int x = 0;
            int y = 0;
            CallResult result;
            byte* errorPtr;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                fixed (byte* layerPtr = layerBytes)
                {
                    result = (CallResult)Native.LoadEXRWithLayer_Export(
                        &img, &x, &y,
                        fileNamePtr,
                        hasLayer ? layerPtr : null,
                        &errorPtr);
                }
            }
            try
            {
                if (result == CallResult.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y);
                    float[] data = imgRef.ToArray();
                    width = x;
                    height = y;
                    rgba = data;
                    error = string.Empty;
                }
                else
                {
                    width = default;
                    height = default;
                    rgba = default!;
                    error = Encoding.UTF8.GetString(errorPtr, (int)StrLen(errorPtr));
                }
            }
            catch (Exception e)
            {
                throw new LoadExrException("managed part exception", e);
            }
            finally
            {
                if (img != null)
                {
                    Native.FreeImageData(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    Native.FreeEXRErrorMessage_Export(errorPtr);
                    errorPtr = null;
                }
            }

            return result;
        }

        private unsafe static bool IsExr(string filename)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            CallResult result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (CallResult)Native.IsEXR_Export((char*)fileNamePtr);
            }

            return result == CallResult.Success;
        }
    }
}
