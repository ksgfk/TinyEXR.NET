using TinyEXR;
using TinyEXR.Viewer.Models;

namespace TinyEXR.Viewer.Services;

internal sealed class ExrDocumentLoader
{
    public async Task<ExrViewerDocument> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        return await Task.Run(() => LoadCore(path, cancellationToken), cancellationToken);
    }

    private static ExrViewerDocument LoadCore(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ResultCode versionResult = Exr.ParseEXRVersionFromFile(path, out ExrVersion version);
        if (versionResult != ResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to parse EXR version: {versionResult}.");
        }

        if (version.Multipart)
        {
            return LoadMultipart(path, version, cancellationToken);
        }

        if (version.NonImage)
        {
            return LoadDeepOrMetadataOnly(path, version, cancellationToken);
        }

        return LoadSingleImage(path, version, cancellationToken);
    }

    private static ExrViewerDocument LoadSingleImage(string path, ExrVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ResultCode headerResult = Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header);
        if (headerResult != ResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to parse EXR header: {headerResult}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        ResultCode imageResult = Exr.LoadEXRImageFromFile(path, header, out ExrImage image);
        if (imageResult != ResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to decode EXR image: {imageResult}.");
        }

        return new ExrViewerDocument
        {
            FilePath = path,
            Version = version,
            Kind = ExrDocumentKind.SingleImage,
            StatusMessage = "Single-part image loaded.",
            Parts =
            [
                CreatePartDocument(0, header, image),
            ],
        };
    }

    private static ExrViewerDocument LoadMultipart(string path, ExrVersion version, CancellationToken cancellationToken)
    {
        byte[] bytes = File.ReadAllBytes(path);
        cancellationToken.ThrowIfCancellationRequested();

        ResultCode headerResult = Exr.ParseEXRMultipartHeaderFromMemory(bytes, out _, out ExrMultipartHeader headers);
        if (headerResult != ResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to parse multipart EXR header: {headerResult}.");
        }

        List<ExrPartDocument> descriptors = headers.Headers
            .Select((header, index) => CreatePartDescriptor(index, header))
            .ToList();

        bool hasOnlyImageParts = !version.NonImage && headers.Headers.All(static header => !header.IsDeep);
        if (hasOnlyImageParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ResultCode loadResult = Exr.LoadEXRMultipartImageFromMemory(bytes, headers, out ExrMultipartImage images);
            if (loadResult == ResultCode.Success && images.Images.Count == headers.Headers.Count)
            {
                List<ExrPartDocument> parts = new(headers.Headers.Count);
                for (int i = 0; i < headers.Headers.Count; i++)
                {
                    parts.Add(CreatePartDocument(i, headers.Headers[i], images.Images[i]));
                }

                return new ExrViewerDocument
                {
                    FilePath = path,
                    Version = version,
                    Kind = ExrDocumentKind.MultipartImage,
                    StatusMessage = $"Multipart image loaded ({parts.Count} parts).",
                    Parts = parts,
                };
            }

            return new ExrViewerDocument
            {
                FilePath = path,
                Version = version,
                Kind = ExrDocumentKind.MetadataOnlyMultipart,
                StatusMessage = $"Multipart metadata loaded, but full image decode failed: {loadResult}.",
                Parts = descriptors,
            };
        }

        return new ExrViewerDocument
        {
            FilePath = path,
            Version = version,
            Kind = ExrDocumentKind.MetadataOnlyMultipart,
            StatusMessage = "Multipart file contains non-image or unsupported parts. Metadata view only.",
            Parts = descriptors,
        };
    }

    private static ExrViewerDocument LoadDeepOrMetadataOnly(string path, ExrVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ResultCode headerResult = Exr.ParseEXRHeaderFromFile(path, out _, out ExrHeader header);
        if (headerResult != ResultCode.Success)
        {
            throw new InvalidOperationException($"Failed to parse EXR header: {headerResult}.");
        }

        if (!header.IsDeep)
        {
            return new ExrViewerDocument
            {
                FilePath = path,
                Version = version,
                Kind = ExrDocumentKind.MetadataOnly,
                StatusMessage = "Non-image EXR metadata loaded. Preview is unavailable for this file.",
                Parts =
                [
                    CreatePartDescriptor(0, header),
                ],
            };
        }

        cancellationToken.ThrowIfCancellationRequested();

        ResultCode deepResult = Exr.LoadDeepEXR(path, out ExrHeader deepHeader, out ExrDeepImage deepImage);
        if (deepResult != ResultCode.Success)
        {
            return new ExrViewerDocument
            {
                FilePath = path,
                Version = version,
                Kind = ExrDocumentKind.MetadataOnly,
                StatusMessage = $"Deep EXR metadata loaded, but deep payload decode failed: {deepResult}.",
                Parts =
                [
                    CreatePartDescriptor(0, header),
                ],
            };
        }

        return new ExrViewerDocument
        {
            FilePath = path,
            Version = version,
            Kind = ExrDocumentKind.DeepSingle,
            StatusMessage = "Deep EXR metadata loaded. 2D preview is unavailable by design.",
            Parts =
            [
                CreatePartDescriptor(0, deepHeader),
            ],
            DeepDocument = new ExrDeepDocument
            {
                Header = deepHeader,
                Image = deepImage,
                Statistics = ComputeDeepStatistics(deepImage),
            },
        };
    }

    private static ExrPartDocument CreatePartDescriptor(int index, ExrHeader header)
    {
        return new ExrPartDocument
        {
            Index = index,
            Header = header,
            HasRootLayer = ExrLayerHelper.HasRootLayer(header.Channels),
            NamedLayers = ExrLayerHelper.GetNamedLayers(header.Channels),
        };
    }

    private static ExrPartDocument CreatePartDocument(int index, ExrHeader header, ExrImage image)
    {
        return new ExrPartDocument
        {
            Index = index,
            Header = header,
            Image = image,
            HasRootLayer = ExrLayerHelper.HasRootLayer(header.Channels),
            NamedLayers = ExrLayerHelper.GetNamedLayers(header.Channels),
        };
    }

    private static DeepStatistics ComputeDeepStatistics(ExrDeepImage image)
    {
        long totalSamples = 0;
        int maxSamplesPerPixel = 0;
        int nonEmptyPixels = 0;

        for (int y = 0; y < image.OffsetTable.Length; y++)
        {
            int[] row = image.OffsetTable[y];
            int previous = 0;
            for (int x = 0; x < row.Length; x++)
            {
                int current = row[x];
                int sampleCount = current - previous;
                previous = current;

                if (sampleCount <= 0)
                {
                    continue;
                }

                totalSamples += sampleCount;
                nonEmptyPixels++;
                if (sampleCount > maxSamplesPerPixel)
                {
                    maxSamplesPerPixel = sampleCount;
                }
            }
        }

        return new DeepStatistics
        {
            TotalSamples = totalSamples,
            MaxSamplesPerPixel = maxSamplesPerPixel,
            NonEmptyPixels = nonEmptyPixels,
        };
    }
}
