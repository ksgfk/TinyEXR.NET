using System;
using System.Text;
using TinyEXR.Native;

namespace TinyEXR
{
    //TODO: ParseEXRMultipartHeader
    //TODO: ParseEXRMultipartImage
    //TODO: SaveEXRMultipartImage
    public static class Exr
    {
        public unsafe static ResultCode LoadEXR(string filename, out float[] rgba, out int width, out int height)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            float* img = null;
            int x = 0;
            int y = 0;
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = (ResultCode)EXRNative.LoadEXRInternal(&img, &x, &y, (sbyte*)fileNamePtr, &errorPtr);
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
            finally
            {
                if (img != null)
                {
                    EXRNative.FreeInternal(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }

        public static unsafe ResultCode LoadEXRWithLayer(string filename, string? layer, out float[] rgba, out int width, out int height)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            if (string.IsNullOrWhiteSpace(layer))
            {
                layer = string.Empty;
            }
            int layerByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> layerBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(layer, layerBytes);
            float* img = null;
            int x = 0;
            int y = 0;
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                fixed (byte* layerPtr = layerBytes)
                {
                    result = (ResultCode)EXRNative.LoadEXRWithLayerInternal(&img, &x, &y, (sbyte*)fileNamePtr, (sbyte*)layerPtr, &errorPtr);
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
            finally
            {
                if (img != null)
                {
                    EXRNative.FreeInternal(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }

        public static unsafe ResultCode EXRLayers(string filename, out string[] layers)
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
                result = (ResultCode)EXRNative.EXRLayersInternal((sbyte*)fileNamePtr, &layerNames, &count, &errorPtr);
            }
            try
            {
                if (result == ResultCode.Success)
                {
                    layers = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        sbyte* layerName = layerNames[i];
                        UIntPtr sizeT = EXRNative.StrLenInternal(layerName);
                        ulong size = sizeT.ToUInt64();
                        if (size >= int.MaxValue)
                        {
                            throw new ArgumentOutOfRangeException(nameof(layerName));
                        }
                        layers[i] = Encoding.UTF8.GetString((byte*)layerName, (int)size);
                    }
                }
                else
                {
                    layers = default!;
                }
            }
            finally
            {
                if (layerNames != null)
                {
                    EXRNative.FreeInternal(layerNames);
                    layerNames = null;
                }
                if (errorPtr != null)
                {
                    EXRNative.FreeEXRErrorMessageInternal(errorPtr);
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
                result = (ResultCode)EXRNative.IsEXRInternal((sbyte*)fileNamePtr);
            }
            return result == ResultCode.Success;
        }

        public unsafe static bool IsExrFromMemory(ReadOnlySpan<byte> data)
        {
            ResultCode result;
            fixed (byte* dataPtr = data)
            {
                result = (ResultCode)EXRNative.IsEXRFromMemoryInternal(dataPtr, new UIntPtr((uint)data.Length));
            }
            return result == ResultCode.Success;
        }

        public unsafe static ResultCode SaveEXRToMemory(
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
                ret = EXRNative.SaveEXRToMemoryInternal(dataPtr, width, height, components, asFp16 ? 1 : 0, &resultPtr, &errorPtr);
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
            finally
            {
                if (resultPtr != null)
                {
                    EXRNative.FreeInternal(resultPtr);
                    resultPtr = null;
                }
                if (errorPtr != null)
                {
                    EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                    errorPtr = null;
                }
            }
            return ret > 0 ? ResultCode.Success : (ResultCode)ret;
        }

        public unsafe static ResultCode SaveEXR(
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
                    result = (ResultCode)EXRNative.SaveEXRInternal(dataPtr, width, height, components, asFp16 ? 1 : 0, (sbyte*)fileNamePtr, &errorPtr);
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe int EXRNumLevels(ref ExrImage image)
        {
            fixed (EXRImage* ptr = &image._img)
            {
                return EXRNative.EXRNumLevelsInternal(ptr);
            }
        }

        public static unsafe void InitEXRHeader(ref ExrHeader header)
        {
            fixed (EXRHeader* ptr = &header._header)
            {
                EXRNative.InitEXRHeaderInternal(ptr);
            }
        }

        public static unsafe void EXRSetNameAttr(ref ExrHeader header, string name)
        {
            int nameByteLength = Encoding.UTF8.GetByteCount(name);
            Span<byte> nameBytes = nameByteLength <= 256 ? stackalloc byte[256] : new byte[nameByteLength];
            Encoding.UTF8.GetBytes(name, nameBytes);
            fixed (byte* nptr = nameBytes)
            {
                fixed (EXRHeader* hptr = &header._header)
                {
                    EXRNative.EXRSetNameAttrInternal(hptr, (sbyte*)nptr);
                }
            }
        }

        public static unsafe void InitEXRImage(ref ExrImage image)
        {
            fixed (EXRImage* ptr = &image._img)
            {
                EXRNative.InitEXRImageInternal(ptr);
            }
        }

        public static unsafe ResultCode FreeEXRHeader(ref ExrHeader header)
        {
            ResultCode result;
            fixed (EXRHeader* ptr = &header._header)
            {
                result = (ResultCode)EXRNative.FreeEXRHeaderInternal(ptr);
            }
            return result;
        }

        public static unsafe ResultCode FreeEXRImage(ref ExrImage image)
        {
            ResultCode result;
            fixed (EXRImage* ptr = &image._img)
            {
                result = (ResultCode)EXRNative.FreeEXRImageInternal(ptr);
            }
            return result;
        }

        public static unsafe void FreeEXRErrorMessage(IntPtr ptr)
        {
            EXRNative.FreeEXRErrorMessageInternal((sbyte*)ptr.ToPointer());
        }

        public unsafe static ResultCode ParseEXRVersionFromFile(string filename, out ExrVersion version)
        {
            int fileNameByteLength = Encoding.ASCII.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            ResultCode result;
            fixed (byte* filenamePtr = fileNameBytes)
            {
                fixed (EXRVersion* versionPtr = &version._version)
                {
                    result = (ResultCode)EXRNative.ParseEXRVersionFromFileInternal(versionPtr, (sbyte*)filenamePtr);
                }
            }
            return result;
        }

        public static unsafe ResultCode ParseEXRVersionFromMemory(ReadOnlySpan<byte> data, out ExrVersion version)
        {
            ResultCode result;
            fixed (byte* dataPtr = data)
            {
                fixed (EXRVersion* versionPtr = &version._version)
                {
                    result = (ResultCode)EXRNative.ParseEXRVersionFromMemoryInternal(versionPtr, dataPtr, new UIntPtr((uint)data.Length));
                }
            }
            return result;
        }

        public static unsafe ResultCode ParseEXRHeaderFromFile(string filename, ref ExrVersion version, out ExrHeader header)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* filenamePtr = fileNameBytes)
            {
                fixed (EXRVersion* versionPtr = &version._version)
                {
                    fixed (EXRHeader* headerPtr = &header._header)
                    {
                        result = (ResultCode)EXRNative.ParseEXRHeaderFromFileInternal(headerPtr, versionPtr, (sbyte*)filenamePtr, &errorPtr);
                    }
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe ResultCode ParseEXRHeaderFromMemory(ReadOnlySpan<byte> data, ref ExrVersion version, out ExrHeader header)
        {
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* dataPtr = data)
            {
                fixed (EXRVersion* versionPtr = &version._version)
                {
                    fixed (EXRHeader* headerPtr = &header._header)
                    {
                        result = (ResultCode)EXRNative.ParseEXRHeaderFromMemoryInternal(headerPtr, versionPtr, dataPtr, new UIntPtr((uint)data.Length), &errorPtr);
                    }
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe ResultCode LoadEXRImageFromFile(ref ExrImage image, ref ExrHeader header, string filename)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* filePtr = fileNameBytes)
            {
                fixed (EXRImage* imagePtr = &image._img)
                {
                    fixed (EXRHeader* headerPtr = &header._header)
                    {
                        result = (ResultCode)EXRNative.LoadEXRImageFromFileInternal(imagePtr, headerPtr, (sbyte*)filePtr, &errorPtr);
                    }
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe ResultCode LoadEXRImageFromMemory(ref ExrImage image, ref ExrHeader header, ReadOnlySpan<byte> data)
        {
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* dataPtr = data)
            {
                fixed (EXRImage* imagePtr = &image._img)
                {
                    fixed (EXRHeader* headerPtr = &header._header)
                    {
                        result = (ResultCode)EXRNative.LoadEXRImageFromMemoryInternal(imagePtr, headerPtr, dataPtr, new UIntPtr((uint)data.Length), &errorPtr);
                    }
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe ResultCode SaveEXRImageToFile(ref ExrImage image, ref ExrHeader header, string filename)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* filePtr = fileNameBytes)
            {
                fixed (EXRImage* imagePtr = &image._img)
                {
                    fixed (EXRHeader* headerPtr = &header._header)
                    {
                        result = (ResultCode)EXRNative.SaveEXRImageToFileInternal(imagePtr, headerPtr, (sbyte*)filePtr, &errorPtr);
                    }
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe byte[]? SaveEXRImageFromMemory(ref ExrImage image, ref ExrHeader header)
        {
            UIntPtr rawLen;
            sbyte* errorPtr;
            byte* dataPtr;
            fixed (EXRImage* imagePtr = &image._img)
            {
                fixed (EXRHeader* headerPtr = &header._header)
                {
                    rawLen = EXRNative.SaveEXRImageToMemoryInternal(imagePtr, headerPtr, &dataPtr, &errorPtr);
                }
            }
            byte[]? result;
            try
            {
                var lenUlong = rawLen.ToUInt64();
                if (lenUlong >= int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(lenUlong));
                }
                int length = (int)lenUlong;

                if (length == 0)
                {
                    result = null;
                }
                else
                {
                    result = new byte[length];
                    Span<byte> src = new Span<byte>(dataPtr, length);
                    src.CopyTo(result);
                }
            }
            finally
            {
                if (errorPtr != null)
                {
                    EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                    errorPtr = null;
                }
                if (dataPtr != null)
                {
                    EXRNative.FreeInternal(dataPtr);
                    dataPtr = null;
                }
            }
            return result;
        }

        public static unsafe ResultCode LoadDeepEXR(ref ExrDeepImage deep, string filename)
        {
            int fileNameByteLength = Encoding.UTF8.GetByteCount(filename);
            Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
            Encoding.UTF8.GetBytes(filename, fileNameBytes);
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* filePtr = fileNameBytes)
            {
                fixed (DeepImage* deepPtr = &deep._deep)
                {
                    result = (ResultCode)EXRNative.LoadDeepEXRInternal(deepPtr, (sbyte*)filePtr, &errorPtr);
                }
            }
            if (errorPtr != null)
            {
                EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                errorPtr = null;
            }
            return result;
        }

        public static unsafe ResultCode LoadEXRFromMemory(ReadOnlySpan<byte> data, out float[] rgba, out int width, out int height)
        {
            float* img = null;
            int x = 0;
            int y = 0;
            ResultCode result;
            sbyte* errorPtr;
            fixed (byte* dataPtr = data)
            {
                result = (ResultCode)EXRNative.LoadEXRFromMemoryInternal(&img, &x, &y, dataPtr, new UIntPtr((uint)data.Length), &errorPtr);
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
            finally
            {
                if (img != null)
                {
                    EXRNative.FreeInternal(img);
                    img = null;
                }
                if (errorPtr != null)
                {
                    EXRNative.FreeEXRErrorMessageInternal(errorPtr);
                    errorPtr = null;
                }
            }
            return result;
        }
    }
}
