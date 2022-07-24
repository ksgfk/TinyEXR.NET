using System;
using System.Text;

namespace TinyEXR.NET
{
    public static class OpenExr
    {
        public static ResultType Load(string filename, out float[] rgba, out int width, out int height, out string error)
        {
            return LoadExr(filename, out rgba, out width, out height, out error);
        }

        public static ResultType Load(string filename, out float[] rgba, out int width, out int height)
        {
            return LoadExr(filename, out rgba, out width, out height, out _);
        }

        public static ResultType Load(string filename, string? layer, out float[] rgba, out int width, out int height, out string error)
        {
            return LoadExrWithLayer(filename, layer, out rgba, out width, out height, out error);
        }

        public static ResultType Load(string filename, string? layer, out float[] rgba, out int width, out int height)
        {
            return LoadExrWithLayer(filename, layer, out rgba, out width, out height, out _);
        }

        public static bool IsExr(string filename)
        {
            return IsExrImpl(filename);
        }

        public static ResultType Save(ReadOnlySpan<float> data, int width, int height, int components, bool asFp16, out byte[] result, out string error)
        {
            return SaveToMemory(data, width, height, components, asFp16, out result, out error);
        }

        public static ResultType Save(ReadOnlySpan<float> data, int width, int height, int components, bool asFp16, out byte[] result)
        {
            return SaveToMemory(data, width, height, components, asFp16, out result, out _);
        }

        public static ResultType Save(ReadOnlySpan<float> data, int width, int height, int components, bool asFp16, string filename, out string error)
        {
            return SaveToFile(data, width, height, components, asFp16, filename, out error);
        }

        public static ResultType Save(ReadOnlySpan<float> data, int width, int height, int components, bool asFp16, string filename)
        {
            return SaveToFile(data, width, height, components, asFp16, filename, out _);
        }

        public static ResultType GetLayers(string filename, out string[] layers, out string error)
        {
            return GetLayersImpl(filename, out layers, out error);
        }

        public static ResultType GetLayers(string filename, out string[] layers)
        {
            return GetLayersImpl(filename, out layers, out _);
        }

        private unsafe static long StrLen(byte* ptr)
        {
            byte* p = ptr;
            for (; *p != '\0'; p++) ;
            return p - ptr;
        }

        private unsafe static ResultType LoadExr(string filename, out float[] rgba, out int width, out int height, out string error)
        {
            int fileNameByteLength = Encoding.ASCII.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            float* img = null;
            int x = 0;
            int y = 0;
            ResultType result;
            byte* errorPtr = null;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultType)Native.LoadEXR_Export(&img, &x, &y, fileNamePtr, &errorPtr);
            }

            try
            {

                if (result == ResultType.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y * 4);
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

        private static unsafe ResultType LoadExrWithLayer(string filename, string? layer, out float[] rgba, out int width, out int height, out string error)
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
            ResultType result;
            byte* errorPtr;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                fixed (byte* layerPtr = layerBytes)
                {
                    result = (ResultType)Native.LoadEXRWithLayer_Export(
                        &img, &x, &y,
                        fileNamePtr,
                        hasLayer ? layerPtr : null,
                        &errorPtr);
                }
            }
            try
            {
                if (result == ResultType.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y * 4);
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

        private unsafe static bool IsExrImpl(string filename)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            ResultType result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultType)Native.IsEXR_Export((char*)fileNamePtr);
            }

            return result == ResultType.Success;
        }

        private unsafe static ResultType SaveToMemory(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            out byte[] outBuffer,
            out string error)
        {
            if (width * height * components != data.Length)
            {
                throw new ArgumentException($"{nameof(data)} length must equal {nameof(width)} * {nameof(height)} * {nameof(components)}");
            }
            byte* resultPtr = null;
            byte* errorPtr = null;
            int ret;
            fixed (float* dataPtr = data)
            {
                ret = Native.SaveEXRToMemory_Export(dataPtr, width, height, components, asFp16 ? 1 : 0, &resultPtr, &errorPtr);
            }
            try
            {
                if (ret > 0)
                {
                    Span<byte> resultData = new Span<byte>(resultPtr, ret);
                    outBuffer = resultData.ToArray();
                    error = string.Empty;
                }
                else
                {
                    outBuffer = default!;
                    error = Encoding.UTF8.GetString(errorPtr, (int)StrLen(errorPtr));
                }
            }
            catch (Exception e)
            {
                throw new LoadExrException("managed part exception", e);
            }
            finally
            {
                if (resultPtr != null)
                {
                    Native.FreeMemory(resultPtr);
                    resultPtr = null;
                }
                if (errorPtr != null)
                {
                    Native.FreeEXRErrorMessage_Export(errorPtr);
                    errorPtr = null;
                }
            }
            return ret > 0 ? ResultType.Success : (ResultType)ret;
        }

        private unsafe static ResultType SaveToFile(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            string filename,
            out string error)
        {
            if (width * height * components != data.Length)
            {
                throw new ArgumentException($"{nameof(data)} length must equal {nameof(width)} * {nameof(height)} * {nameof(components)}");
            }
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            byte* errorPtr = null;
            ResultType result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                fixed (float* dataPtr = data)
                {
                    result = (ResultType)Native.SaveEXR_Export(dataPtr, width, height, components, asFp16 ? 1 : 0, fileNamePtr, &errorPtr);
                }
            }

            try
            {
                if (result == ResultType.Success)
                {
                    error = string.Empty;
                }
                else
                {
                    error = Encoding.UTF8.GetString(errorPtr, (int)StrLen(errorPtr));
                }
            }
            catch (Exception e)
            {
                throw new LoadExrException("managed part exception", e);
            }
            finally
            {
                if (errorPtr != null)
                {
                    Native.FreeEXRErrorMessage_Export(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }

        private static unsafe ResultType GetLayersImpl(string filename, out string[] layers, out string error)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            byte** layerNames;
            int count;
            byte* errorPtr;
            ResultType result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultType)Native.EXRLayers_Export(fileNamePtr, &layerNames, &count, &errorPtr);
            }

            try
            {
                if (result == ResultType.Success)
                {
                    layers = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        byte* layerName = layerNames[i];
                        layers[i] = Encoding.UTF8.GetString(layerName, (int)StrLen(layerName));
                    }
                    error = string.Empty;
                }
                else
                {
                    layers = default!;
                    error = Encoding.UTF8.GetString(errorPtr, (int)StrLen(errorPtr));
                }
            }
            catch (Exception e)
            {
                throw new LoadExrException("managed part exception", e);
            }
            finally
            {
                if (layerNames != null)
                {
                    Native.FreeMemory(layerNames);
                    layerNames = null;
                }
                if (errorPtr != null)
                {
                    Native.FreeEXRErrorMessage_Export(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }
    }
}
