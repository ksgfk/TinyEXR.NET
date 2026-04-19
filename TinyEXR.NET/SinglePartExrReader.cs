using System;
using System.Numerics;

namespace TinyEXR
{
    public class SinglePartExrReader
    {
        private byte[][] _channelData = Array.Empty<byte[]>();
        private ExrChannel[] _channels = Array.Empty<ExrChannel>();

        public int Width { get; private set; }

        public int Height { get; private set; }

        public ExrChannel[] Channels => _channels;

        public float PixelAspectRatio { get; private set; }

        public ExrBox2i DataWindow { get; private set; }

        public ExrBox2i DisplayWindow { get; private set; }

        public CompressionType Compression { get; private set; }

        public LineOrderType LineOrder { get; private set; }

        public Vector2 ScreenWindowCenter { get; private set; }

        public float ScreenWindowWidth { get; private set; }

        public void Read(string path)
        {
            ResultCode result = Exr.TryReadImage(path, out ExrHeader header, out ExrImage image);
            ThrowOnFailure(result, path);
            ProcessImage(header, image);
        }

        public void Read(ReadOnlySpan<byte> data)
        {
            ResultCode result = Exr.TryReadImage(data, out ExrHeader header, out ExrImage image);
            ThrowOnFailure(result, null);
            ProcessImage(header, image);
        }

        public ReadOnlySpan<byte> GetImageData(int channel)
        {
            if (channel < 0 || channel >= _channelData.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }

            return _channelData[channel];
        }

        public ReadOnlySpan<byte> GetImageData(string channelName)
        {
            for (int i = 0; i < _channels.Length; i++)
            {
                if (string.Equals(_channels[i].Name, channelName, StringComparison.Ordinal))
                {
                    return _channelData[i];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(channelName));
        }

        private void ProcessImage(ExrHeader header, ExrImage image)
        {
            _channels = new ExrChannel[image.Channels.Count];
            _channelData = new byte[image.Channels.Count][];
            for (int i = 0; i < image.Channels.Count; i++)
            {
                ExrImageChannel channel = image.Channels[i];
                _channels[i] = new ExrChannel(
                    channel.Channel.Name,
                    channel.Channel.Type,
                    channel.Channel.SamplingX,
                    channel.Channel.SamplingY,
                    channel.Channel.Linear);
                _channelData[i] = channel.Data;
            }

            Width = image.Width;
            Height = image.Height;
            PixelAspectRatio = header.PixelAspectRatio;
            DataWindow = header.DataWindow;
            DisplayWindow = header.DisplayWindow;
            Compression = header.Compression;
            LineOrder = header.LineOrder;
            ScreenWindowCenter = header.ScreenWindowCenter;
            ScreenWindowWidth = header.ScreenWindowWidth;
        }

        private static void ThrowOnFailure(ResultCode result, string? path)
        {
            if (result == ResultCode.Success)
            {
                return;
            }

            string message = path == null
                ? $"cannot read EXR image, result: {result}"
                : $"cannot read EXR image, result: {result}, file: {path}";

            if (result == ResultCode.UnsupportedFeature)
            {
                throw new NotSupportedException(message);
            }

            throw new InvalidOperationException(message);
        }
    }
}
