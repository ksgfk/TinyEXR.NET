using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyEXR
{
    public sealed class ExrImageChannel
    {
        public ExrImageChannel(ExrChannel channel, ExrPixelType dataType, byte[] data)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            DataType = dataType;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public ExrChannel Channel { get; }

        public ExrPixelType DataType { get; }

        public byte[] Data { get; }
    }

    public sealed class ExrImage
    {
        public ExrImage(int width, int height, IEnumerable<ExrImageChannel> channels)
        {
            Width = width;
            Height = height;
            Channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
        }

        public int Width { get; }

        public int Height { get; }

        public IList<ExrImageChannel> Channels { get; }

        public ExrImageChannel GetChannel(string name)
        {
            foreach (ExrImageChannel channel in Channels)
            {
                if (string.Equals(channel.Channel.Name, name, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name));
        }
    }
}
