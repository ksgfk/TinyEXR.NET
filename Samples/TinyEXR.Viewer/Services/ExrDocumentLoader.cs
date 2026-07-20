using System.Buffers.Binary;
using TinyEXR.Viewer.Models;
using V3 = TinyEXR.V3;
using V3IO = TinyEXR.V3.IO;

namespace TinyEXR.Viewer.Services;

internal sealed class ExrDocumentLoader
{
    private const int PrefixByteCount = 8;
    private const uint TiledFlag = 1U << 9;
    private const uint LongNamesFlag = 1U << 10;
    private const uint NonImageFlag = 1U << 11;
    private const uint MultipartFlag = 1U << 12;

    public async Task<ExrViewerDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.RandomAccess);

        byte[] prefix = new byte[PrefixByteCount];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        if (!V3.ExrFile.IsExr(prefix))
        {
            throw new InvalidDataException("The file does not have an OpenEXR signature.");
        }

        ExrVersionInfo version = ParseVersion(prefix);
        stream.Position = 0;

        await using V3IO.StreamDataSource source = new(stream, leaveOpen: true);
        await using V3.ExrReader reader = V3.ExrReader.OpenAsyncSource(
            source,
            new V3.ReaderOptions(leaveOpen: true));

        V3.ReaderResult parseResult = await reader.ParseHeaderAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!parseResult.IsSuccess)
        {
            throw new InvalidDataException(
                $"Failed to parse EXR headers: {FormatFailure(parseResult)}.",
                parseResult.Error);
        }

        List<ExrPartDocument> parts = new(reader.NumParts);
        for (int partIndex = 0; partIndex < reader.NumParts; partIndex++)
        {
            V3.Header header = reader.GetHeader(partIndex);
            V3.ReaderResult<V3.Part> readResult = await reader.ReadPartAsync(
                partIndex,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            parts.Add(readResult.IsSuccess && readResult.Value is V3.Part part
                ? CreatePartDocument(partIndex, header, part)
                : CreatePartDescriptor(partIndex, header, FormatFailure(readResult.Operation)));
        }

        return new ExrViewerDocument
        {
            FilePath = path,
            Version = version,
            Kind = GetDocumentKind(parts),
            StatusMessage = BuildStatus(parts),
            Parts = parts,
        };
    }

    private static ExrVersionInfo ParseVersion(ReadOnlySpan<byte> prefix)
    {
        uint rawValue = BinaryPrimitives.ReadUInt32LittleEndian(prefix.Slice(sizeof(uint)));
        return new ExrVersionInfo
        {
            RawValue = rawValue,
            FileVersion = (int)(rawValue & 0xffU),
            Tiled = (rawValue & TiledFlag) != 0,
            LongNames = (rawValue & LongNamesFlag) != 0,
            NonImage = (rawValue & NonImageFlag) != 0,
            Multipart = (rawValue & MultipartFlag) != 0,
        };
    }

    private static ExrDocumentKind GetDocumentKind(IReadOnlyList<ExrPartDocument> parts)
    {
        if (parts.Count > 1)
        {
            return parts.All(static part => part.HasMaterializedData)
                ? ExrDocumentKind.MultipartImage
                : ExrDocumentKind.MetadataOnlyMultipart;
        }

        ExrPartDocument part = parts[0];
        if (!part.HasMaterializedData)
        {
            return ExrDocumentKind.MetadataOnly;
        }

        return part.Header.IsDeep
            ? ExrDocumentKind.DeepSingle
            : ExrDocumentKind.SingleImage;
    }

    private static string BuildStatus(IReadOnlyList<ExrPartDocument> parts)
    {
        int decodedCount = parts.Count(static part => part.HasMaterializedData);
        int deepCount = parts.Count(static part => part.Header.IsDeep);
        int flatCount = parts.Count - deepCount;
        if (decodedCount == parts.Count)
        {
            return parts.Count == 1
                ? deepCount == 1
                    ? "Deep EXR loaded through TinyEXR.V3. A 2D preview is unavailable by design."
                    : "Single-part image loaded through TinyEXR.V3."
                : $"Multipart EXR loaded through TinyEXR.V3 ({parts.Count} parts: {flatCount} flat, {deepCount} deep).";
        }

        string failures = string.Join(
            "; ",
            parts.Where(static part => !part.HasMaterializedData)
                .Select(static part => $"#{part.Index} {part.DecodeStatus}"));
        return $"Parsed {parts.Count} part headers and decoded {decodedCount}; unavailable parts: {failures}.";
    }

    private static ExrPartDocument CreatePartDescriptor(
        int index,
        V3.Header header,
        string decodeStatus)
    {
        return new ExrPartDocument
        {
            Index = index,
            Header = header,
            DecodeStatus = decodeStatus,
            HasRootLayer = ExrLayerHelper.HasRootLayer(header.Channels),
            NamedLayers = ExrLayerHelper.GetNamedLayers(header.Channels),
        };
    }

    private static ExrPartDocument CreatePartDocument(
        int index,
        V3.Header header,
        V3.Part part)
    {
        return new ExrPartDocument
        {
            Index = index,
            Header = header,
            Part = part,
            DecodeStatus = "Decoded",
            DeepStatistics = header.IsDeep ? ComputeDeepStatistics(part) : null,
            HasRootLayer = ExrLayerHelper.HasRootLayer(header.Channels),
            NamedLayers = ExrLayerHelper.GetNamedLayers(header.Channels),
        };
    }

    private static DeepStatistics ComputeDeepStatistics(V3.Part part)
    {
        int levelCount = 0;
        long pixelLocations = 0;
        long nonEmptyPixels = 0;
        int maxSamplesPerPixel = 0;
        ulong totalSamples = 0;

        foreach (V3.PartLevel partLevel in part.Levels)
        {
            if (partLevel is not V3.DeepLevel level)
            {
                continue;
            }

            levelCount++;
            ReadOnlySpan<int> counts = level.SampleCounts;
            pixelLocations = checked(pixelLocations + counts.Length);
            totalSamples = checked(totalSamples + level.TotalSamples);
            for (int pixelIndex = 0; pixelIndex < counts.Length; pixelIndex++)
            {
                int sampleCount = counts[pixelIndex];
                if (sampleCount == 0)
                {
                    continue;
                }

                nonEmptyPixels++;
                maxSamplesPerPixel = Math.Max(maxSamplesPerPixel, sampleCount);
            }
        }

        return new DeepStatistics
        {
            LevelCount = levelCount,
            PixelLocations = pixelLocations,
            NonEmptyPixels = nonEmptyPixels,
            MaxSamplesPerPixel = maxSamplesPerPixel,
            TotalSamples = totalSamples,
        };
    }

    private static string FormatFailure(V3.ReaderResult result)
    {
        return result.Error is null
            ? result.Status.ToString()
            : $"{result.Status}: {result.Error.Message}";
    }
}
