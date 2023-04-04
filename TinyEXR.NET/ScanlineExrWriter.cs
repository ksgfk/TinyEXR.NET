using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TinyEXR.Native;

namespace TinyEXR
{
    public class ScanlineExrWriter
    {
        private class ChannelData
        {
            public ExrChannel Channel;
            public byte[] Data;
            public ExrPixelType DataType;

            public ChannelData(ExrChannel channel, byte[] data, ExrPixelType dataType)
            {
                Channel = channel ?? throw new ArgumentNullException(nameof(channel));
                Data = data ?? throw new ArgumentNullException(nameof(data));
                DataType = dataType;
            }
        }

        readonly List<ChannelData> _channels;
        int _width;
        int _height;
        CompressionType _compression;

        public ScanlineExrWriter()
        {
            _channels = new List<ChannelData>();
            _compression = CompressionType.ZIP;
            _width = 0;
            _height = 0;
        }

        public ScanlineExrWriter AddChannel(string name, ExrPixelType saveType, byte[] data, ExrPixelType dataType)
        {
            _channels.Add(new ChannelData(new ExrChannel(name, saveType), data, dataType));
            return this;
        }

        public ScanlineExrWriter SetCompression(CompressionType type)
        {
            _compression = type;
            return this;
        }

        public ScanlineExrWriter SetSize(int width, int height)
        {
            _width = width;
            _height = height;
            return this;
        }

        private unsafe byte[]? SaveFunc(Func<EXRHeader, EXRImage, byte[]?> func)
        {
            if (_compression == CompressionType.PXR24 || _compression == CompressionType.B44 || _compression == CompressionType.B44A)
            {
                throw new InvalidOperationException($"tinyexr unsupport compression type: {_compression}");
            }
            if (_width == 0 || _height == 0)
            {
                throw new InvalidOperationException($"image size cannot be zero");
            }
            EXRChannelInfo[] chlist = new EXRChannelInfo[_channels.Count];
            int[] reqType = new int[_channels.Count];
            int[] saveType = new int[_channels.Count];
            EXRHeader header = default;
            Exr.InitEXRHeader(ref header);
            EXRImage image = default;
            Exr.InitEXRImage(ref image);
            var dataMemorys = _channels.Select(i => i.Data.AsMemory()).ToArray();
            fixed (EXRChannelInfo* chPtr = chlist)
            {
                fixed (int* reqPtr = reqType)
                {
                    fixed (int* savePtr = saveType)
                    {
                        for (int i = 0; i < chlist.Length; i++)
                        {
                            Encoding.UTF8.GetBytes(_channels[i].Channel.Name, new Span<byte>(chPtr[i].name, 256));
                            chlist[i].pixel_type = (int)_channels[i].Channel.Type;
                            chlist[i].x_sampling = _channels[i].Channel.SamplingX;
                            chlist[i].y_sampling = _channels[i].Channel.SamplingY;
                            chlist[i].p_linear = _channels[i].Channel.Linear;
                            reqType[i] = (int)_channels[i].DataType;
                            saveType[i] = (int)_channels[i].Channel.Type;
                        }
                        header.num_channels = _channels.Count;
                        header.channels = chPtr;
                        header.requested_pixel_types = reqPtr;
                        header.pixel_types = savePtr;
                        header.compression_type = (int)_compression;

                        var dataMemHandle = dataMemorys.Select(i => i.Pin()).ToArray();
                        var dataPtrArr = dataMemHandle.Select(i => new IntPtr(i.Pointer)).ToArray();
                        fixed (IntPtr* dataPtr = dataPtrArr)
                        {
                            image.num_channels = _channels.Count;
                            image.images = (byte**)dataPtr;
                            image.width = _width;
                            image.height = _height;
                        }
                        try
                        {
                            return func(header, image);
                        }
                        finally
                        {
                            Array.ForEach(dataMemHandle, i => i.Dispose());
                        }
                    }
                }
            }
        }

        public byte[]? Save()
        {
            return SaveFunc((header, image) =>
            {
                byte[]? result = Exr.SaveEXRImageToMemory(ref image, ref header);
                return result ?? throw new InvalidOperationException("cannot save image to memory");
            });
        }

        public void Save(string path)
        {
            SaveFunc((header, image) =>
            {
                ResultCode result = Exr.SaveEXRImageToFile(ref image, ref header, path);
                if (result != ResultCode.Success)
                {
                    throw new InvalidOperationException($"cannot save image to file, reason: {result}");
                }
                return null;
            });
        }
    }
}
