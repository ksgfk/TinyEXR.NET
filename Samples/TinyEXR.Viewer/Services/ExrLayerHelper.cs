using TinyEXR;

namespace TinyEXR.Viewer.Services;

internal static class ExrLayerHelper
{
    public static bool HasRootLayer(IList<ExrChannel> channels)
    {
        for (int i = 0; i < channels.Count; i++)
        {
            if (IsInRootLayer(channels[i].Name))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> GetNamedLayers(IList<ExrChannel> channels)
    {
        List<string> layers = new();
        for (int i = 0; i < channels.Count; i++)
        {
            string channelName = channels[i].Name;
            int separator = channelName.LastIndexOf('.');
            if (separator <= 0 || separator + 1 >= channelName.Length)
            {
                continue;
            }

            string layer = channelName.Substring(0, separator);
            if (!layers.Contains(layer, StringComparer.Ordinal))
            {
                layers.Add(layer);
            }
        }

        return layers;
    }

    public static IReadOnlyList<LayerChannelMatch> MatchLayer(IList<ExrImageChannel> channels, string? layerName)
    {
        List<LayerChannelMatch> matches = new();
        string effectiveLayer = string.IsNullOrWhiteSpace(layerName) ? string.Empty : layerName;

        for (int i = 0; i < channels.Count; i++)
        {
            ExrImageChannel channel = channels[i];
            string strippedName = channel.Channel.Name;
            if (effectiveLayer.Length == 0)
            {
                int separator = strippedName.LastIndexOf('.');
                if (separator > 0)
                {
                    continue;
                }

                if (separator == 0 && separator + 1 < strippedName.Length)
                {
                    strippedName = strippedName[(separator + 1)..];
                }
            }
            else
            {
                string prefix = effectiveLayer + ".";
                if (!strippedName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                strippedName = strippedName[prefix.Length..];
            }

            matches.Add(new LayerChannelMatch(strippedName, channel));
        }

        return matches;
    }

    private static bool IsInRootLayer(string channelName)
    {
        int separator = channelName.LastIndexOf('.');
        return separator <= 0;
    }
}

internal sealed class LayerChannelMatch
{
    public LayerChannelMatch(string name, ExrImageChannel channel)
    {
        Name = name;
        Channel = channel;
    }

    public string Name { get; }

    public ExrImageChannel Channel { get; }
}
