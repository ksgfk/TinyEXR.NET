using System;
using System.Collections.Generic;
using System.IO;

namespace TinyEXR
{
    public class ScanlineExrWriter
    {
        private sealed class ChannelData
        {
            public ChannelData(ExrChannel channel, byte[] data, ExrPixelType dataType)
            {
                Channel = channel ?? throw new ArgumentNullException(nameof(channel));
                Data = data ?? throw new ArgumentNullException(nameof(data));
                DataType = dataType;
            }

            public ExrChannel Channel { get; }

            public byte[] Data { get; }

            public ExrPixelType DataType { get; }
        }

        private readonly List<ChannelData> _channels = new List<ChannelData>();
        private int _width;
        private int _height;
        private CompressionType _compression = CompressionType.ZIP;

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

        public byte[] Save()
        {
            ExrImage image = BuildImage();
            ExrHeader header = BuildHeader();

            ResultCode result = Exr.SaveEXRImageToMemory(image, header, out byte[] encoded);
            if (result == ResultCode.Success)
            {
                return encoded;
            }

            ThrowOnFailure(result, null);
            return Array.Empty<byte>();
        }

        public void Save(string path)
        {
            ExrImage image = BuildImage();
            ExrHeader header = BuildHeader();
            ResultCode result = Exr.SaveEXRImageToFile(image, header, path);
            if (result != ResultCode.Success)
            {
                ThrowOnFailure(result, path);
            }
        }

        private ExrImage BuildImage()
        {
            if (_width <= 0 || _height <= 0)
            {
                throw new InvalidOperationException("image size cannot be zero");
            }

            if (_channels.Count == 0)
            {
                throw new InvalidOperationException("at least one channel is required");
            }

            ExrImageChannel[] channels = new ExrImageChannel[_channels.Count];
            for (int i = 0; i < _channels.Count; i++)
            {
                ChannelData channel = _channels[i];
                channels[i] = new ExrImageChannel(channel.Channel, channel.DataType, channel.Data);
            }

            return new ExrImage(_width, _height, channels);
        }

        private ExrHeader BuildHeader()
        {
            return new ExrHeader
            {
                Compression = _compression,
            };
        }

        private static void ThrowOnFailure(ResultCode result, string? path)
        {
            string message = path == null
                ? $"cannot save image, reason: {result}"
                : $"cannot save image, reason: {result}, file: {path}";

            if (result == ResultCode.UnsupportedFeature)
            {
                throw new NotSupportedException(message);
            }

            throw new InvalidOperationException(message);
        }
    }
}
