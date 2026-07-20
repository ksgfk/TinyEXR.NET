using V3 = TinyEXR.V3;

namespace TinyEXR.Viewer.Services;

internal static class ExrLayerHelper
{
    public static bool HasRootLayer(IReadOnlyList<V3.Channel> channels)
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

    public static IReadOnlyList<string> GetNamedLayers(IReadOnlyList<V3.Channel> channels)
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

    public static IReadOnlyList<LayerChannelMatch> MatchLayer(
        IReadOnlyList<V3.Channel> descriptions,
        V3.PartLevel level,
        string? layerName)
    {
        if (descriptions.Count != level.Channels.Count)
        {
            throw new InvalidOperationException("The level channels do not match the part header.");
        }

        List<LayerChannelMatch> matches = new();
        string effectiveLayer = string.IsNullOrWhiteSpace(layerName) ? string.Empty : layerName;

        for (int i = 0; i < level.Channels.Count; i++)
        {
            V3.Channel description = descriptions[i];
            V3.ChannelBuffer buffer = level.Channels[i];
            if (!StringComparer.Ordinal.Equals(description.Name, buffer.Name))
            {
                throw new InvalidOperationException("The level channels do not match the part header.");
            }

            string strippedName = description.Name;
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

            matches.Add(new LayerChannelMatch(strippedName, description, buffer));
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
    public LayerChannelMatch(string name, V3.Channel description, V3.ChannelBuffer buffer)
    {
        Name = name;
        Description = description;
        Buffer = buffer;
    }

    public string Name { get; }

    public V3.Channel Description { get; }

    public V3.ChannelBuffer Buffer { get; }
}
