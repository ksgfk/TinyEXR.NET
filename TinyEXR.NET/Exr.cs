using System;
using TinyEXR.Native;

namespace TinyEXR
{
    //TODO: ParseEXRMultipartHeader
    //TODO: ParseEXRMultipartImage
    //TODO: SaveEXRMultipartImage
    public static class Exr
    {
        private const int WavelengthBufferSize = 32;
        private const int ChannelNameBufferSize = 64;

        public unsafe static ResultCode LoadEXR(string filename, out float[] rgba, out int width, out int height)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
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
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            if (string.IsNullOrWhiteSpace(layer))
            {
                layer = string.Empty;
            }
            byte[] layerBytes = NativeUtf8.ToNullTerminated(layer);
            float* img = null;
            int x = 0;
            int y = 0;
            ResultCode result;
            sbyte* errorPtr = null;
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
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            sbyte** layerNames = null;
            int count = 0;
            sbyte* errorPtr = null;
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
                        layers[i] = NativeUtf8.Read(layerNames[i]);
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
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
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
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
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

        public static unsafe int EXRNumLevels(ref EXRImage image)
        {
            fixed (EXRImage* ptr = &image)
            {
                return EXRNative.EXRNumLevelsInternal(ptr);
            }
        }

        public static unsafe void InitEXRHeader(ref EXRHeader header)
        {
            fixed (EXRHeader* ptr = &header)
            {
                EXRNative.InitEXRHeaderInternal(ptr);
            }
        }

        public static unsafe void EXRSetNameAttr(ref EXRHeader header, string name)
        {
            byte[] nameBytes = NativeUtf8.ToNullTerminated(name);
            fixed (byte* nptr = nameBytes)
            {
                fixed (EXRHeader* hptr = &header)
                {
                    EXRNative.EXRSetNameAttrInternal(hptr, (sbyte*)nptr);
                }
            }
        }

        public static unsafe void InitEXRImage(ref EXRImage image)
        {
            fixed (EXRImage* ptr = &image)
            {
                EXRNative.InitEXRImageInternal(ptr);
            }
        }

        public static unsafe ResultCode FreeEXRHeader(ref EXRHeader header)
        {
            ResultCode result;
            fixed (EXRHeader* ptr = &header)
            {
                result = (ResultCode)EXRNative.FreeEXRHeaderInternal(ptr);
            }
            return result;
        }

        public static unsafe ResultCode FreeEXRImage(ref EXRImage image)
        {
            ResultCode result;
            fixed (EXRImage* ptr = &image)
            {
                result = (ResultCode)EXRNative.FreeEXRImageInternal(ptr);
            }
            return result;
        }

        public static unsafe void FreeEXRErrorMessage(IntPtr ptr)
        {
            EXRNative.FreeEXRErrorMessageInternal((sbyte*)ptr.ToPointer());
        }

        public unsafe static ResultCode ParseEXRVersionFromFile(string filename, out EXRVersion version)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            ResultCode result;
            fixed (byte* filenamePtr = fileNameBytes)
            {
                fixed (EXRVersion* versionPtr = &version)
                {
                    result = (ResultCode)EXRNative.ParseEXRVersionFromFileInternal(versionPtr, (sbyte*)filenamePtr);
                }
            }
            return result;
        }

        public static unsafe ResultCode ParseEXRVersionFromMemory(ReadOnlySpan<byte> data, out EXRVersion version)
        {
            ResultCode result;
            fixed (byte* dataPtr = data)
            {
                fixed (EXRVersion* versionPtr = &version)
                {
                    result = (ResultCode)EXRNative.ParseEXRVersionFromMemoryInternal(versionPtr, dataPtr, new UIntPtr((uint)data.Length));
                }
            }
            return result;
        }

        public static unsafe ResultCode ParseEXRHeaderFromFile(string filename, ref EXRVersion version, ref EXRHeader header)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* filenamePtr = fileNameBytes)
            {
                fixed (EXRVersion* versionPtr = &version)
                {
                    fixed (EXRHeader* headerPtr = &header)
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

        public static unsafe ResultCode ParseEXRHeaderFromMemory(ReadOnlySpan<byte> data, ref EXRVersion version, ref EXRHeader header)
        {
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* dataPtr = data)
            {
                fixed (EXRVersion* versionPtr = &version)
                {
                    fixed (EXRHeader* headerPtr = &header)
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

        public static unsafe ResultCode LoadEXRImageFromFile(ref EXRImage image, ref EXRHeader header, string filename)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* filePtr = fileNameBytes)
            {
                fixed (EXRImage* imagePtr = &image)
                {
                    fixed (EXRHeader* headerPtr = &header)
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

        public static unsafe ResultCode LoadEXRImageFromMemory(ref EXRImage image, ref EXRHeader header, ReadOnlySpan<byte> data)
        {
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* dataPtr = data)
            {
                fixed (EXRImage* imagePtr = &image)
                {
                    fixed (EXRHeader* headerPtr = &header)
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

        public static unsafe ResultCode SaveEXRImageToFile(ref EXRImage image, ref EXRHeader header, string filename)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* filePtr = fileNameBytes)
            {
                fixed (EXRImage* imagePtr = &image)
                {
                    fixed (EXRHeader* headerPtr = &header)
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

        public static unsafe byte[]? SaveEXRImageToMemory(ref EXRImage image, ref EXRHeader header)
        {
            UIntPtr rawLen;
            sbyte* errorPtr = null;
            byte* dataPtr = null;
            fixed (EXRImage* imagePtr = &image)
            {
                fixed (EXRHeader* headerPtr = &header)
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

        public static unsafe ResultCode LoadDeepEXR(ref DeepImage deep, string filename)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            ResultCode result;
            sbyte* errorPtr = null;
            fixed (byte* filePtr = fileNameBytes)
            {
                fixed (DeepImage* deepPtr = &deep)
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
            sbyte* errorPtr = null;
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

        public unsafe static bool IsSpectralEXR(string filename)
        {
            byte[] fileNameBytes = NativeUtf8.ToNullTerminated(filename);
            int result;
            fixed (byte* fileNamePtr = fileNameBytes)
            {
                result = EXRNative.IsSpectralEXRInternal((sbyte*)fileNamePtr);
            }

            return result == EXRNative.TINYEXR_SUCCESS;
        }

        public unsafe static bool IsSpectralEXRFromMemory(ReadOnlySpan<byte> data)
        {
            int result;
            fixed (byte* dataPtr = data)
            {
                result = EXRNative.IsSpectralEXRFromMemoryInternal(dataPtr, new UIntPtr((uint)data.Length));
            }

            return result == EXRNative.TINYEXR_SUCCESS;
        }

        public static unsafe SpectrumType? EXRGetSpectrumType(ref EXRHeader header)
        {
            fixed (EXRHeader* headerPtr = &header)
            {
                int value = EXRNative.EXRGetSpectrumTypeInternal(headerPtr);
                if (value < 0)
                {
                    return null;
                }

                return (SpectrumType)value;
            }
        }

        public static unsafe string EXRFormatWavelength(float wavelengthNm)
        {
            Span<byte> buffer = stackalloc byte[WavelengthBufferSize];
            fixed (byte* ptr = buffer)
            {
                EXRNative.EXRFormatWavelengthInternal((sbyte*)ptr, new UIntPtr(WavelengthBufferSize), wavelengthNm);
                return NativeUtf8.Read((sbyte*)ptr);
            }
        }

        public static unsafe string EXRSpectralChannelName(float wavelengthNm, int stokesComponent)
        {
            Span<byte> buffer = stackalloc byte[ChannelNameBufferSize];
            fixed (byte* ptr = buffer)
            {
                EXRNative.EXRSpectralChannelNameInternal((sbyte*)ptr, new UIntPtr(ChannelNameBufferSize), wavelengthNm, stokesComponent);
                return NativeUtf8.Read((sbyte*)ptr);
            }
        }

        public static unsafe string EXRReflectiveChannelName(float wavelengthNm)
        {
            Span<byte> buffer = stackalloc byte[ChannelNameBufferSize];
            fixed (byte* ptr = buffer)
            {
                EXRNative.EXRReflectiveChannelNameInternal((sbyte*)ptr, new UIntPtr(ChannelNameBufferSize), wavelengthNm);
                return NativeUtf8.Read((sbyte*)ptr);
            }
        }

        public static unsafe float EXRParseSpectralChannelWavelength(string channelName)
        {
            byte[] channelNameBytes = NativeUtf8.ToNullTerminated(channelName);
            fixed (byte* ptr = channelNameBytes)
            {
                return EXRNative.EXRParseSpectralChannelWavelengthInternal((sbyte*)ptr);
            }
        }

        public static unsafe int EXRGetStokesComponent(string channelName)
        {
            byte[] channelNameBytes = NativeUtf8.ToNullTerminated(channelName);
            fixed (byte* ptr = channelNameBytes)
            {
                return EXRNative.EXRGetStokesComponentInternal((sbyte*)ptr);
            }
        }

        public static unsafe bool EXRIsSpectralChannel(string channelName)
        {
            byte[] channelNameBytes = NativeUtf8.ToNullTerminated(channelName);
            fixed (byte* ptr = channelNameBytes)
            {
                return EXRNative.EXRIsSpectralChannelInternal((sbyte*)ptr) != 0;
            }
        }

        public static unsafe float[] EXRGetWavelengths(ref EXRHeader header)
        {
            if (header.num_channels <= 0)
            {
                return Array.Empty<float>();
            }

            float[] wavelengths = new float[header.num_channels];
            fixed (EXRHeader* headerPtr = &header)
            {
                fixed (float* wavelengthsPtr = wavelengths)
                {
                    int count = EXRNative.EXRGetWavelengthsInternal(headerPtr, wavelengthsPtr, wavelengths.Length);
                    if (count <= 0)
                    {
                        return Array.Empty<float>();
                    }

                    if (count == wavelengths.Length)
                    {
                        return wavelengths;
                    }

                    float[] result = new float[count];
                    Array.Copy(wavelengths, result, count);
                    return result;
                }
            }
        }

        public static unsafe ResultCode EXRSetSpectralAttributes(ref EXRHeader header,
                                                                 SpectrumType spectrumType,
                                                                 string units)
        {
            byte[] unitsBytes = NativeUtf8.ToNullTerminated(units);
            fixed (EXRHeader* headerPtr = &header)
            {
                fixed (byte* unitsPtr = unitsBytes)
                {
                    return (ResultCode)EXRNative.EXRSetSpectralAttributesInternal(headerPtr, (int)spectrumType, (sbyte*)unitsPtr);
                }
            }
        }

        public static unsafe string? EXRGetSpectralUnits(ref EXRHeader header)
        {
            fixed (EXRHeader* headerPtr = &header)
            {
                sbyte* unitsPtr = EXRNative.EXRGetSpectralUnitsInternal(headerPtr);
                return unitsPtr == null ? null : NativeUtf8.Read(unitsPtr);
            }
        }

        //------------some helper functions------------
        public static unsafe string ReadExrChannelInfoName(ref EXRChannelInfo info)
        {
            fixed (sbyte* ptr = info.name)
            {
                return NativeUtf8.Read(ptr);
            }
        }

        public static int TypeSize(ExrPixelType type)
        {
            return type switch
            {
                ExrPixelType.UInt => 4,
                ExrPixelType.Half => 2,
                ExrPixelType.Float => 4,
                _ => 0,
            };
        }
    }
}
