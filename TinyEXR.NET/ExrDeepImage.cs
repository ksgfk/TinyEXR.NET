using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyEXR
{
    public sealed class ExrDeepChannel
    {
        public ExrDeepChannel(string name, float[][] rows)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        public string Name { get; }

        public float[][] Rows { get; }
    }

    public sealed class ExrDeepImage
    {
        public ExrDeepImage(int width, int height, int[][] offsetTable, IEnumerable<ExrDeepChannel> channels)
        {
            Width = width;
            Height = height;
            OffsetTable = offsetTable ?? throw new ArgumentNullException(nameof(offsetTable));
            Channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
        }

        public int Width { get; }

        public int Height { get; }

        public int[][] OffsetTable { get; }

        public IList<ExrDeepChannel> Channels { get; }
    }
}
