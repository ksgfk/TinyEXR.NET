using System.Collections.Generic;
using System.Numerics;

namespace TinyEXR
{
    public sealed class ExrHeader
    {
        public string? Name { get; set; }

        public string? PartType { get; internal set; }

        public CompressionType Compression { get; set; } = CompressionType.None;

        public LineOrderType LineOrder { get; set; } = LineOrderType.IncreasingY;

        public float PixelAspectRatio { get; set; } = 1.0f;

        public Vector2 ScreenWindowCenter { get; set; } = Vector2.Zero;

        public float ScreenWindowWidth { get; set; } = 1.0f;

        public ExrBox2i DataWindow { get; set; } = new ExrBox2i(0, 0, 0, 0);

        public ExrBox2i DisplayWindow { get; set; } = new ExrBox2i(0, 0, 0, 0);

        public ExrTileDescription? Tiles { get; set; }

        public bool IsDeep { get; internal set; }

        public bool IsMultipart { get; internal set; }

        public bool HasLongNames { get; set; }

        internal int ChunkCount { get; set; }

        internal int HeaderLength { get; set; }

        public IList<ExrChannel> Channels { get; } = new List<ExrChannel>();

        public IList<ExrAttribute> CustomAttributes { get; } = new List<ExrAttribute>();

        internal ExrHeader CloneShallow()
        {
            ExrHeader clone = new ExrHeader
            {
                Name = Name,
                PartType = PartType,
                Compression = Compression,
                LineOrder = LineOrder,
                PixelAspectRatio = PixelAspectRatio,
                ScreenWindowCenter = ScreenWindowCenter,
                ScreenWindowWidth = ScreenWindowWidth,
                DataWindow = DataWindow,
                DisplayWindow = DisplayWindow,
                IsDeep = IsDeep,
                IsMultipart = IsMultipart,
                HasLongNames = HasLongNames,
                ChunkCount = ChunkCount,
                HeaderLength = HeaderLength,
                Tiles = Tiles == null ? null : new ExrTileDescription
                {
                    TileSizeX = Tiles.TileSizeX,
                    TileSizeY = Tiles.TileSizeY,
                    LevelMode = Tiles.LevelMode,
                    RoundingMode = Tiles.RoundingMode,
                },
            };

            foreach (ExrChannel channel in Channels)
            {
                clone.Channels.Add(new ExrChannel(channel.Name, channel.Type, channel.RequestedPixelType, channel.SamplingX, channel.SamplingY, channel.Linear));
            }

            foreach (ExrAttribute attribute in CustomAttributes)
            {
                clone.CustomAttributes.Add(new ExrAttribute(attribute.Name, attribute.TypeName, (byte[])attribute.Value.Clone()));
            }

            return clone;
        }
    }
}
