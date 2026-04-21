using TinyEXR;

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

    public required ExrVersion Version { get; init; }

    public required ExrDocumentKind Kind { get; init; }

    public string StatusMessage { get; init; } = string.Empty;

    public IReadOnlyList<ExrPartDocument> Parts { get; init; } = Array.Empty<ExrPartDocument>();

    public ExrDeepDocument? DeepDocument { get; init; }
}

public sealed class ExrPartDocument
{
    public required int Index { get; init; }

    public required ExrHeader Header { get; init; }

    public ExrImage? Image { get; init; }

    public required bool HasRootLayer { get; init; }

    public IReadOnlyList<string> NamedLayers { get; init; } = Array.Empty<string>();

    public bool CanPreview => Image is not null;

    public string DisplayName
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(Header.Name) ? $"part[{Index}]" : Header.Name!;
            string partType = string.IsNullOrWhiteSpace(Header.PartType)
                ? (Header.Tiles is null ? "scanlineimage" : "tiledimage")
                : Header.PartType!;
            return $"#{Index} {name} ({partType})";
        }
    }
}

public sealed class ExrDeepDocument
{
    public required ExrHeader Header { get; init; }

    public required ExrDeepImage Image { get; init; }

    public required DeepStatistics Statistics { get; init; }
}

public sealed class DeepStatistics
{
    public int NonEmptyPixels { get; init; }

    public int MaxSamplesPerPixel { get; init; }

    public long TotalSamples { get; init; }
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
