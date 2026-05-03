using System.Globalization;
using System.Text;
using TinyEXR;
using TinyEXR.Viewer.Models;

namespace TinyEXR.Viewer.Services;

internal sealed class MetadataFormatter
{
    public IReadOnlyList<KeyValueItem> BuildOverview(
        ExrViewerDocument? document,
        ExrPartDocument? selectedPart,
        LevelOption? selectedLevel,
        LayerOption? selectedLayer)
    {
        if (document is null)
        {
            return Array.Empty<KeyValueItem>();
        }

        List<KeyValueItem> entries = new()
        {
            Entry("Path", document.FilePath),
            Entry("Document", document.Kind.ToString()),
            Entry("Status", document.StatusMessage),
            Entry("Version", document.Version.Version.ToString()),
            Entry("Multipart", document.Version.Multipart.ToString()),
            Entry("Non-image", document.Version.NonImage.ToString()),
            Entry("Tiled Flag", document.Version.Tiled.ToString()),
            Entry("Long Names", document.Version.LongName.ToString()),
        };

        if (selectedPart is not null)
        {
            ExrHeader header = selectedPart.Header;
            entries.Add(Entry("Selected Part", selectedPart.DisplayName));
            entries.Add(Entry("Part Type", header.PartType ?? (header.Tiles is null ? "scanlineimage" : "tiledimage")));
            entries.Add(Entry("Compression", header.Compression.ToString()));
            entries.Add(Entry("Line Order", header.LineOrder.ToString()));
            entries.Add(Entry("Data Window", FormatBox(header.DataWindow)));
            entries.Add(Entry("Display Window", FormatBox(header.DisplayWindow)));
            entries.Add(Entry("Pixel Aspect", header.PixelAspectRatio.ToString("0.###")));
            entries.Add(Entry("Screen Center", $"{header.ScreenWindowCenter.X:0.###}, {header.ScreenWindowCenter.Y:0.###}"));
            entries.Add(Entry("Screen Width", header.ScreenWindowWidth.ToString("0.###")));
            entries.Add(Entry("Deep", header.IsDeep.ToString()));
            entries.Add(Entry("Multipart Header", header.IsMultipart.ToString()));

            if (header.Tiles is not null)
            {
                entries.Add(Entry("Tiles", $"{header.Tiles.TileSizeX} x {header.Tiles.TileSizeY}"));
                entries.Add(Entry("Level Mode", header.Tiles.LevelMode.ToString()));
                entries.Add(Entry("Rounding", header.Tiles.RoundingMode.ToString()));
            }

            if (selectedLevel is not null)
            {
                entries.Add(Entry("Selected Level", selectedLevel.Label));
            }

            if (selectedLayer is not null)
            {
                entries.Add(Entry("Selected Layer", selectedLayer.Label));
            }
        }

        return entries;
    }

    public IReadOnlyList<PartInfoItem> BuildPartEntries(IReadOnlyList<ExrPartDocument> parts)
    {
        return parts.Select(static part => new PartInfoItem
        {
            Title = part.DisplayName,
            Summary = BuildPartSummary(part),
        }).ToArray();
    }

    public IReadOnlyList<string> BuildLayerEntries(ExrPartDocument? part)
    {
        if (part is null)
        {
            return Array.Empty<string>();
        }

        List<string> entries = new();
        if (part.HasRootLayer)
        {
            entries.Add("(root)");
        }

        entries.AddRange(part.NamedLayers);
        return entries.Count == 0 ? ["(none)"] : entries;
    }

    public IReadOnlyList<ChannelInfoItem> BuildChannelEntries(ExrPartDocument? part, LevelOption? selectedLevel)
    {
        if (part is null)
        {
            return Array.Empty<ChannelInfoItem>();
        }

        if (part.Image is not null && selectedLevel is not null &&
            (uint)selectedLevel.LevelIndex < (uint)part.Image.Levels.Count)
        {
            ExrImageLevel level = part.Image.Levels[selectedLevel.LevelIndex];
            return level.Channels.Select(static channel => new ChannelInfoItem
            {
                Name = channel.Channel.Name,
                DataType = channel.DataType.ToString(),
                Sampling = $"{channel.Channel.SamplingX} x {channel.Channel.SamplingY}",
                Storage = $"Stored={channel.Channel.Type}, Requested={channel.Channel.RequestedPixelType}",
                Notes = $"Linear={channel.Channel.Linear}, Bytes={channel.Data.Length}",
            }).ToArray();
        }

        return part.Header.Channels.Select(static channel => new ChannelInfoItem
        {
            Name = channel.Name,
            DataType = "(not loaded)",
            Sampling = $"{channel.SamplingX} x {channel.SamplingY}",
            Storage = $"Stored={channel.Type}, Requested={channel.RequestedPixelType}",
            Notes = $"Linear={channel.Linear}",
        }).ToArray();
    }

    public IReadOnlyList<AttributeInfoItem> BuildAttributeEntries(ExrPartDocument? part)
    {
        if (part is null)
        {
            return Array.Empty<AttributeInfoItem>();
        }

        return part.Header.CustomAttributes.Select(static attribute => new AttributeInfoItem
        {
            Name = attribute.Name,
            TypeName = attribute.TypeName,
            Value = FormatAttribute(attribute),
        }).ToArray();
    }

    public IReadOnlyList<KeyValueItem> BuildDeepEntries(ExrDeepDocument? deepDocument)
    {
        if (deepDocument is null)
        {
            return Array.Empty<KeyValueItem>();
        }

        return
        [
            Entry("Dimensions", $"{deepDocument.Image.Width} x {deepDocument.Image.Height}"),
            Entry("Channels", string.Join(", ", deepDocument.Image.Channels.Select(static channel => channel.Name))),
            Entry("Rows", deepDocument.Image.OffsetTable.Length.ToString()),
            Entry("Non-empty Pixels", deepDocument.Statistics.NonEmptyPixels.ToString()),
            Entry("Max Samples / Pixel", deepDocument.Statistics.MaxSamplesPerPixel.ToString()),
            Entry("Total Samples", deepDocument.Statistics.TotalSamples.ToString()),
        ];
    }

    private static KeyValueItem Entry(string key, string value)
    {
        return new KeyValueItem { Key = key, Value = value };
    }

    private static string BuildPartSummary(ExrPartDocument part)
    {
        List<string> segments =
        [
            $"Channels={part.Header.Channels.Count}",
            $"Compression={part.Header.Compression}",
            $"DataWindow={part.Header.DataWindow.Width}x{part.Header.DataWindow.Height}",
            part.CanPreview ? "Previewable" : "Metadata only",
        ];

        if (part.Header.Tiles is not null)
        {
            segments.Add($"Tiles={part.Header.Tiles.TileSizeX}x{part.Header.Tiles.TileSizeY}");
            segments.Add(part.Header.Tiles.LevelMode.ToString());
        }

        if (part.NamedLayers.Count > 0)
        {
            segments.Add($"Layers={part.NamedLayers.Count + (part.HasRootLayer ? 1 : 0)}");
        }

        return string.Join(" | ", segments);
    }

    private static string FormatAttribute(ExrAttribute attribute)
    {
        if (string.Equals(attribute.TypeName, "string", StringComparison.Ordinal))
        {
            return attribute.ReadString();
        }

        if (string.Equals(attribute.TypeName, "int", StringComparison.Ordinal) &&
            attribute.Value.Length >= sizeof(int))
        {
            return attribute.ReadInt().ToString();
        }

        if (string.Equals(attribute.TypeName, "float", StringComparison.Ordinal) &&
            attribute.Value.Length >= sizeof(float))
        {
            return attribute.ReadFloat().ToString("G9", CultureInfo.InvariantCulture);
        }

        if (string.Equals(attribute.TypeName, "double", StringComparison.Ordinal) &&
            attribute.Value.Length >= sizeof(double))
        {
            return attribute.ReadDouble().ToString("G17", CultureInfo.InvariantCulture);
        }

        if (attribute.Value.Length == 0)
        {
            return "(empty)";
        }

        const int maxBytes = 32;
        int count = Math.Min(attribute.Value.Length, maxBytes);
        StringBuilder builder = new();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(attribute.Value[i].ToString("X2"));
        }

        if (attribute.Value.Length > maxBytes)
        {
            builder.Append(" ...");
        }

        return builder.ToString();
    }

    private static string FormatBox(ExrBox2i box)
    {
        return $"[{box.MinX}, {box.MinY}] - [{box.MaxX}, {box.MaxY}] ({box.Width} x {box.Height})";
    }
}
