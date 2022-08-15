using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TinyEXR
{
    public class ExrImageWriter
    {
        readonly int _width;
        readonly int _height;
        readonly byte[][] _datas;
        readonly ExrPixelType[] _inputTypes;
        readonly ExrPixelType[] _saveTypes;
        readonly string[] _chNames;
        readonly Native.EXRChannelInfo[] _channelInfos;
        CompressionType _comprType;

        public int Width => _width;
        public int Height => _height;

        public ExrImageWriter(int channelCount, int width, int height)
        {
            _width = width;
            _height = height;
            _datas = new byte[channelCount][];
            _inputTypes = new ExrPixelType[channelCount];
            _saveTypes = new ExrPixelType[channelCount];
            _chNames = new string[channelCount];
            _channelInfos = new Native.EXRChannelInfo[channelCount];
            _comprType = CompressionType.ZIP;
        }

        public ExrImageWriter SetChannelInputType(int channel, ExrPixelType type)
        {
            _inputTypes[channel] = type;
            _datas[channel] = new byte[_width * _height * Exr.TypeSize(type)];
            return this;
        }

        public ExrImageWriter SetChannelSaveType(int channel, ExrPixelType type)
        {
            _saveTypes[channel] = type;
            return this;
        }

        public ExrImageWriter SetChannelData(int channel, ReadOnlySpan<float> data)
        {
            if (data.Length * 4 != _datas[channel].Length)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }
            MemoryMarshal.AsBytes(data).CopyTo(_datas[channel]);
            return this;
        }

        public ExrImageWriter SetChannelName(int channel, string name)
        {
            _chNames[channel] = name;
            return this;
        }

        public ExrImageWriter SetChannel(int channel, string name, ReadOnlySpan<float> data)
        {
            return SetChannelInputType(channel, ExrPixelType.Float)
                .SetChannelSaveType(channel, ExrPixelType.Float)
                .SetChannelName(channel, name)
                .SetChannelData(channel, data);
        }

        public ExrImageWriter SetCompressionType(CompressionType type)
        {
            _comprType = type;
            return this;
        }

        private class SaveInfo
        {
            public Native.EXRHeader Header;
            public Native.EXRImage Image;
        }

        private unsafe ResultCode Write(Func<SaveInfo, ResultCode> saveFunc)
        {
            SaveInfo saveInfo = new SaveInfo();

            using var chInfoHandler = _channelInfos.AsMemory().Pin();
            Native.EXRChannelInfo* chInfoPtr = (Native.EXRChannelInfo*)chInfoHandler.Pointer;
            using var inputTypesHandler = _inputTypes.Select(t => (int)t).ToArray().AsMemory().Pin();
            int* inputTypePtr = (int*)inputTypesHandler.Pointer;
            using var saveTypesHandler = _saveTypes.Select(t => (int)t).ToArray().AsMemory().Pin();
            int* saveTypesPtr = (int*)saveTypesHandler.Pointer;

            ref Native.EXRHeader header = ref saveInfo.Header;
            header.num_channels = _datas.Length;
            for (int i = 0; i < _datas.Length; i++)
            {
                Encoding.UTF8.GetBytes(_chNames[i], new Span<byte>(chInfoPtr[i].name, 256));
            }
            header.channels = new IntPtr(chInfoPtr);
            header.requested_pixel_types = new IntPtr(inputTypePtr);
            header.pixel_types = new IntPtr(saveTypesPtr);
            header.compression_type = (int)_comprType;

            var dataHandler = _datas.Select(d => d.AsMemory().Pin()).ToArray();
            ResultCode result;
            try
            {
                ref Native.EXRImage image = ref saveInfo.Image;
                image.num_channels = _datas.Length;
                IntPtr[] imageData = new IntPtr[_datas.Length];
                for (int i = 0; i < _datas.Length; i++)
                {
                    imageData[i] = new IntPtr(dataHandler[i].Pointer);
                }
                using var imageDataHandler = imageData.AsMemory().Pin();
                image.images = new IntPtr(imageDataHandler.Pointer);
                image.width = _width;
                image.height = _height;

                result = saveFunc(saveInfo);
            }
            catch (Exception e)
            {
                throw new TinyExrException(e);
            }
            finally
            {
                foreach (var handler in dataHandler)
                {
                    handler.Dispose();
                }
            }
            return result;
        }

        public unsafe ResultCode WriteToFile(string file)
        {
            return Write(info =>
            {
                ref Native.EXRHeader header = ref info.Header;
                ref Native.EXRImage image = ref info.Image;

                int fileNameByteLength = Encoding.ASCII.GetByteCount(file);
                Span<byte> fileNameBytes = fileNameByteLength <= 256 ? stackalloc byte[256] : new byte[fileNameByteLength];
                Encoding.UTF8.GetBytes(file, fileNameBytes);

                ResultCode code;
                sbyte* errorPtr = null;
                fixed (Native.EXRHeader* headerPtr = &header)
                {
                    fixed (Native.EXRImage* imagePtr = &image)
                    {
                        IntPtr img = new IntPtr(imagePtr);
                        IntPtr hed = new IntPtr(headerPtr);

                        fixed (byte* filePtr = fileNameBytes)
                        {
                            code = (ResultCode)Native.SaveEXRImageToFileInternal(img, hed, filePtr, &errorPtr);
                        }
                    }
                }
                if (errorPtr != null)
                {
                    Native.GlobalFree(errorPtr);
                    errorPtr = null;
                }
                return code;
            });
        }

        public unsafe byte[] WriteToMemory()
        {
            byte[] realResult = null!;
            Write(info =>
            {
                ref Native.EXRHeader header = ref info.Header;
                ref Native.EXRImage image = ref info.Image;

                sbyte* errorPtr = null;
                byte* dataPtr = null;
                ulong size = 0;
                fixed (Native.EXRHeader* headerPtr = &header)
                {
                    fixed (Native.EXRImage* imagePtr = &image)
                    {
                        IntPtr img = new IntPtr(imagePtr);
                        IntPtr hed = new IntPtr(headerPtr);

                        size = Native.SaveEXRImageToMemoryInternal(img, hed, &dataPtr, &errorPtr);
                    }
                }
                byte[] result;
                try
                {
                    if (size >= int.MaxValue)
                    {
                        throw new TinyExrException($"file too big {size}");
                    }
                    result = new Span<byte>(dataPtr, (int)size).ToArray();
                }
                catch (Exception e)
                {
                    throw new TinyExrException(e);
                }
                finally
                {
                    if (errorPtr != null)
                    {
                        Native.GlobalFree(errorPtr);
                        errorPtr = null;
                    }
                    if (dataPtr != null)
                    {
                        Native.GlobalFree(dataPtr);
                        dataPtr = null;
                    }
                }
                realResult = result;
                return ResultCode.Success;
            });
            return realResult;
        }
    }
}
