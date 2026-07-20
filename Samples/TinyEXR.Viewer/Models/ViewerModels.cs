using V3 = TinyEXR.V3;

namespace TinyEXR.Viewer.Models;

public enum ExrDocumentKind
{
    SingleImage,
    MultipartImage,
    MetadataOnlyMultipart,
    DeepSingle,
    MetadataOnly,
}

public sealed class ExrViewerDocument
{
    public required string FilePath { get; init; }

    public required ExrVersionInfo Version { get; init; }

    public required ExrDocumentKind Kind { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public IReadOnlyList<ExrPartDocument> Parts { get; init; } = Array.Empty<ExrPartDocument>();
}

public sealed class ExrVersionInfo
{
    public required uint RawValue { get; init; }

    public required int FileVersion { get; init; }

    public required bool Tiled { get; init; }

    public required bool LongNames { get; init; }

    public required bool NonImage { get; init; }

    public required bool Multipart { get; init; }
}

public sealed class ExrPartDocument
{
    public required int Index { get; init; }

    public required V3.Header Header { get; init; }

    public V3.Part? Part { get; init; }

    public string DecodeStatus { get; init; } = "Not decoded";

    public DeepStatistics? DeepStatistics { get; init; }

    public required bool HasRootLayer { get; init; }

    public IReadOnlyList<string> NamedLayers { get; init; } = Array.Empty<string>();

    public bool CanPreview => Part is not null && !Header.IsDeep;

    public bool HasMaterializedData => Part is not null;

    public string DisplayName
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(Header.Name) ? $"part[{Index}]" : Header.Name;
            return $"#{Index} {name} ({Header.PartType})";
        }
    }
}

public sealed class DeepStatistics
{
    public int LevelCount { get; init; }

    public long PixelLocations { get; init; }

    public long NonEmptyPixels { get; init; }

    public int MaxSamplesPerPixel { get; init; }

    public ulong TotalSamples { get; init; }
}

public sealed class PreviewBuffer
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required float[] LinearRgba { get; init; }
}

public sealed class PartOption
{
    public required int PartIndex { get; init; }

    public required string Label { get; init; }
}

public sealed class LayerOption
{
    public required string Label { get; init; }

    public string? LayerName { get; init; }
}

public sealed class LevelOption
{
    public required int LevelIndex { get; init; }

    public required string Label { get; init; }
}

public sealed class KeyValueItem
{
    public required string Key { get; init; }

    public required string Value { get; init; }
}

public sealed class PartInfoItem
{
    public required string Title { get; init; }

    public required string Summary { get; init; }
}

public sealed class ChannelInfoItem
{
    public required string Name { get; init; }

    public required string DataType { get; init; }

    public required string Sampling { get; init; }

    public required string Storage { get; init; }

    public required string Notes { get; init; }
}

public sealed class AttributeInfoItem
{
    public required string Name { get; init; }

    public required string TypeName { get; init; }

    public required string Value { get; init; }
}
