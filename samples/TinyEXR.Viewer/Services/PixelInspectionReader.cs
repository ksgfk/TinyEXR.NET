using System.Globalization;
using TinyEXR.Viewer.Models;
using V3 = TinyEXR;

namespace TinyEXR.Viewer.Services;

internal static class PixelInspectionReader
{
    public static PixelInspection? Inspect(ExrPartDocument part, int levelIndex, string? layerName, int x, int y)
    {
        if (part.Part is null || (uint)levelIndex >= (uint)part.Part.Levels.Count ||
            part.Part.Levels[levelIndex] is not V3.FlatLevel level ||
            x < 0 || y < 0 || x >= level.Width || y >= level.Height)
        {
            return null;
        }

        int exrX = checked((int)((long)level.Region.MinX + x));
        int exrY = checked((int)((long)level.Region.MinY + y));
        IReadOnlyList<LayerChannelMatch> matches = ExrLayerHelper.MatchLayer(part.Header.Channels, level, layerName);
        List<PixelChannelValue> values = new(matches.Count);
        foreach (LayerChannelMatch match in matches)
        {
            ChannelSample? sample = ExrChannelSampler.Read(match, level.Region, x, y);
            values.Add(new PixelChannelValue
            {
                Name = match.Description.Name,
                DataType = match.Buffer.PixelType.ToString(),
                Value = sample is ChannelSample value
                    ? value.Value.ToString(match.Buffer.PixelType == V3.PixelType.UInt ? "F0" : "G9", CultureInfo.InvariantCulture)
                    : "No sample",
                SampleLocation = sample is ChannelSample location && (location.X != exrX || location.Y != exrY)
                    ? $"Sample at EXR ({location.X}, {location.Y})"
                    : string.Empty,
            });
        }

        return new PixelInspection
        {
            X = x,
            Y = y,
            ExrX = exrX,
            ExrY = exrY,
            Context = $"{part.DisplayName} · {layerName ?? "(root)"} · L({level.LevelX}, {level.LevelY})",
            Channels = values,
        };
    }
}
