using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyEXR
{
    public sealed class ExrTile
    {
        public ExrTile(
            int offsetX,
            int offsetY,
            int levelX,
            int levelY,
            int width,
            int height,
            IEnumerable<ExrImageChannel> channels)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            LevelX = levelX;
            LevelY = levelY;
            Width = width;
            Height = height;
            Channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
        }

        public int OffsetX { get; }

        public int OffsetY { get; }

        public int LevelX { get; }

        public int LevelY { get; }

        public int Width { get; }

        public int Height { get; }

        public IList<ExrImageChannel> Channels { get; }
    }

    public sealed class ExrImageLevel
    {
        public ExrImageLevel(
            int levelX,
            int levelY,
            int width,
            int height,
            IEnumerable<ExrImageChannel> channels,
            IEnumerable<ExrTile>? tiles = null)
        {
            LevelX = levelX;
            LevelY = levelY;
            Width = width;
            Height = height;
            Channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
            Tiles = tiles?.ToList() ?? new List<ExrTile>();
        }

        public int LevelX { get; }

        public int LevelY { get; }

        public int Width { get; }

        public int Height { get; }

        public IList<ExrImageChannel> Channels { get; }

        public IList<ExrTile> Tiles { get; }
    }

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
            : this(new[] { new ExrImageLevel(0, 0, width, height, channels) })
        {
        }

        public ExrImage(IEnumerable<ExrImageLevel> levels)
        {
            Levels = levels?.ToList() ?? throw new ArgumentNullException(nameof(levels));
            if (Levels.Count == 0)
            {
                throw new ArgumentException("At least one image level is required.", nameof(levels));
            }

            ExrImageLevel baseLevel = Levels[0];
            Width = baseLevel.Width;
            Height = baseLevel.Height;
            Channels = baseLevel.Channels;
        }

        public int Width { get; }

        public int Height { get; }

        public IList<ExrImageChannel> Channels { get; }

        public IList<ExrImageLevel> Levels { get; }

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
