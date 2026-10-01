namespace TinyEXR.Viewer.Models;

public sealed class PixelInspection
{
    public required int X { get; init; }

    public required int Y { get; init; }

    public required int ExrX { get; init; }

    public required int ExrY { get; init; }

    public required string Context { get; init; }

    public required IReadOnlyList<PixelChannelValue> Channels { get; init; }

    public string Coordinates => $"Pixel ({X}, {Y}) · EXR ({ExrX}, {ExrY})";
}

public sealed class PixelChannelValue
{
    public required string Name { get; init; }

    public required string DataType { get; init; }

    public required string Value { get; init; }

    public string SampleLocation { get; init; } = string.Empty;

    public bool HasSampleLocation => SampleLocation.Length > 0;
}
