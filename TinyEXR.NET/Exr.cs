using System;
using System.Text;

namespace TinyEXR
{
    public static class Exr
    {
        private unsafe static long StrLen(byte* ptr)
        {
            byte* p = ptr;
            for (; *p != '\0'; p++) ;
            return p - ptr;
        }

        public unsafe static ResultCode LoadFromFile(string filename, out float[] rgba, out int width, out int height)
        {
            int fileNameByteLength = Encoding.ASCII.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            float* img = null;
            int x = 0;
            int y = 0;
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultCode)Native.LoadEXRInternal(&img, &x, &y, fileNamePtr, &errorPtr);
            }

            try
            {
                if (result == ResultCode.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y * 4);
                    float[] data = imgRef.ToArray();
                    width = x;
                    height = y;
                    rgba = data;
                }
                else
                {
                    width = default;
                    height = default;
                    rgba = default!;
                }
            }
            catch (Exception e)
            {
                throw new TinyExrException(e);
            }
            finally
            {
                if (img != null)
                {
                    Native.GlobalFree(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    Native.GlobalFree(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }

        public static unsafe ResultCode LoadFromFileWithLayers(string filename, string? layer, out float[] rgba, out int width, out int height)
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
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                fixed (byte* layerPtr = layerBytes)
                {
                    result = (ResultCode)Native.LoadEXRWithLayerInternal(
                        &img, &x, &y,
                        fileNamePtr,
                        hasLayer ? layerPtr : null,
                        &errorPtr);
                }
            }
            try
            {
                if (result == ResultCode.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y * 4);
                    float[] data = imgRef.ToArray();
                    width = x;
                    height = y;
                    rgba = data;
                }
                else
                {
                    width = default;
                    height = default;
                    rgba = default!;
                }
            }
            catch (Exception e)
            {
                throw new TinyExrException(e);
            }
            finally
            {
                if (img != null)
                {
                    Native.GlobalFree(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    Native.GlobalFree(errorPtr);
                    errorPtr = null;
                }
            }

            return result;
        }

        public static unsafe ResultCode LoadFromMemory(ReadOnlySpan<byte> data, out float[] rgba, out int width, out int height)
        {
            float* img = null;
            int x = 0;
            int y = 0;
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* dataPtr = data)
            {
                result = (ResultCode)Native.LoadEXRFromMemoryInternal(&img, &x, &y, dataPtr, (ulong)data.Length, &errorPtr);
            }
            try
            {
                if (result == ResultCode.Success)
                {
                    Span<float> imgRef = new Span<float>(img, x * y * 4);
                    float[] load = imgRef.ToArray();
                    width = x;
                    height = y;
                    rgba = load;
                }
                else
                {
                    width = default;
                    height = default;
                    rgba = default!;
                }
            }
            catch (Exception e)
            {
                throw new TinyExrException(e);
            }
            finally
            {
                if (img != null)
                {
                    Native.GlobalFree(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    Native.GlobalFree(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }

        public static unsafe ResultCode GetLayers(string filename, out string[] layers)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            sbyte** layerNames;
            int count;
            sbyte* errorPtr;
            ResultCode result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultCode)Native.EXRLayersInternal(fileNamePtr, &layerNames, &count, &errorPtr);
            }

            try
            {
                if (result == ResultCode.Success)
                {
                    layers = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        sbyte* layerName = layerNames[i];
                        layers[i] = Encoding.UTF8.GetString((byte*)layerName, (int)StrLen((byte*)layerName));
                    }
                }
                else
                {
                    layers = default!;
                }
            }
            catch (Exception e)
            {
                throw new TinyExrException(e);
            }
            finally
            {
                if (layerNames != null)
                {
                    Native.GlobalFree(layerNames);
                    layerNames = null;
                }
                if (errorPtr != null)
                {
                    Native.GlobalFree(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }

        public unsafe static bool IsExr(string filename)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            ResultCode result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultCode)Native.IsEXRInternal(fileNamePtr);
            }

            return result == ResultCode.Success;
        }

        public unsafe static bool IsExr(ReadOnlySpan<byte> data)
        {
            ResultCode result;
            fixed (byte* dataPtr = data)
            {
                result = (ResultCode)Native.IsEXRFromMemoryInternal(dataPtr, (ulong)data.Length);
            }
            return result == ResultCode.Success;
        }

        public unsafe static ResultCode SaveToMemory(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            out byte[] outBuffer)
        {
            if (width * height * components != data.Length)
            {
                throw new ArgumentException($"{nameof(data)} length must equal {nameof(width)} * {nameof(height)} * {nameof(components)}");
            }
            byte* resultPtr = null;
            sbyte* errorPtr = null;
            int ret;
            fixed (float* dataPtr = data)
            {
                ret = Native.SaveEXRToMemoryInternal(dataPtr, width, height, components, asFp16 ? 1 : 0, &resultPtr, &errorPtr);
            }
            try
            {
                if (ret > 0)
                {
                    Span<byte> resultData = new Span<byte>(resultPtr, ret);
                    outBuffer = resultData.ToArray();
                }
                else
                {
                    outBuffer = default!;
                }
            }
            catch (Exception e)
            {
                throw new TinyExrException("managed part exception", e);
            }
            finally
            {
                if (resultPtr != null)
                {
                    Native.GlobalFree(resultPtr);
                    resultPtr = null;
                }
                if (errorPtr != null)
                {
                    Native.GlobalFree(errorPtr);
                    errorPtr = null;
                }
            }
            return ret > 0 ? ResultCode.Success : (ResultCode)ret;
        }

        public unsafe static ResultCode SaveToFile(
            ReadOnlySpan<float> data,
            int width,
            int height,
            int components,
            bool asFp16,
            string filename)
        {
            if (width * height * components != data.Length)
            {
                throw new ArgumentException($"{nameof(data)} length must equal {nameof(width)} * {nameof(height)} * {nameof(components)}");
            }
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);

            sbyte* errorPtr = null;
            ResultCode result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                fixed (float* dataPtr = data)
                {
                    result = (ResultCode)Native.SaveEXRInternal(dataPtr, width, height, components, asFp16 ? 1 : 0, fileNamePtr, &errorPtr);
                }
            }

            if (errorPtr != null)
            {
                Native.GlobalFree(errorPtr);
                errorPtr = null;
            }
            return result;
        }
    }
}
