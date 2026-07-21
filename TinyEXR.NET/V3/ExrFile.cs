using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyEXR.V3.IO;

namespace TinyEXR.V3
{
    /// <summary>
    /// High-level whole-image operations corresponding to tinyexr v3's load/save layer.
    /// </summary>
    public static class ExrFile
    {
        public const int ApiVersionMajor = 3;
        public const int ApiVersionMinor = 0;
        public const int ApiVersionPatch = 0;

        private const uint Magic = 20_000_630U;

        public static bool IsExr(ReadOnlySpan<byte> data)
        {
            return data.Length >= sizeof(uint) &&
                BinaryPrimitives.ReadUInt32LittleEndian(data) == Magic;
        }

        public static ReaderResult<Image> LoadFromMemory(
            ReadOnlyMemory<byte> data,
            ReaderOptions? options = null)
        {
            using ExrReader reader = ExrReader.OpenMemory(data, options);
            return ReadImage(reader);
        }

        public static ReaderResult<Image> LoadFromStream(
            Stream stream,
            ReaderOptions? options = null)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            ReaderOptions effectiveOptions = options ?? new ReaderOptions();
            long origin = stream.Position;
            using StreamDataSource source = new StreamDataSource(
                stream,
                origin,
                leaveOpen: effectiveOptions.LeaveOpen);
            using ExrReader reader = ExrReader.OpenSource(
                source,
                new ReaderOptions(effectiveOptions.Limits, leaveOpen: true));
            return ReadImage(reader);
        }

        public static async ValueTask<ReaderResult<Image>> LoadFromStreamAsync(
            Stream stream,
            ReaderOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            ReaderOptions effectiveOptions = options ?? new ReaderOptions();
            long origin = stream.Position;
            await using StreamDataSource source = new StreamDataSource(
                stream,
                origin,
                leaveOpen: effectiveOptions.LeaveOpen);
            await using ExrReader reader = ExrReader.OpenAsyncSource(
                source,
                new ReaderOptions(effectiveOptions.Limits, leaveOpen: true));
            return await ReadImageAsync(reader, cancellationToken).ConfigureAwait(false);
        }

        public static ReaderResult<Image> LoadFromFile(
            string path,
            ReaderOptions? options = null)
        {
            ValidatePath(path);
            try
            {
                using FileStream stream = File.OpenRead(path);
                ReaderOptions effectiveOptions = options ?? new ReaderOptions();
                return LoadFromStream(
                    stream,
                    new ReaderOptions(effectiveOptions.Limits, leaveOpen: false));
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return ReaderFailure<Image>(ExrResult.IO, exception);
            }
        }

        public static async ValueTask<ReaderResult<Image>> LoadFromFileAsync(
            string path,
            ReaderOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(path);
            try
            {
                using FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                ReaderOptions effectiveOptions = options ?? new ReaderOptions();
                return await LoadFromStreamAsync(
                    stream,
                    new ReaderOptions(effectiveOptions.Limits, leaveOpen: false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return ReaderFailure<Image>(ExrResult.IO, exception);
            }
        }

        public static WriterResult<byte[]> SaveToMemory(
            Image image,
            WriterOptions? options = null)
        {
            return SaveToMemoryCore(image, compression: null, options);
        }

        public static WriterResult<byte[]> SaveToMemory(
            Image image,
            Compression compression,
            WriterOptions? options = null)
        {
            ModelValidation.ValidateEnum(compression, nameof(compression));
            return SaveToMemoryCore(image, compression, options);
        }

        public static WriterResult SaveToStream(
            Image image,
            Stream stream,
            WriterOptions? options = null)
        {
            return SaveToStreamCore(image, stream, compression: null, options);
        }

        public static WriterResult SaveToStream(
            Image image,
            Stream stream,
            Compression compression,
            WriterOptions? options = null)
        {
            ModelValidation.ValidateEnum(compression, nameof(compression));
            return SaveToStreamCore(image, stream, compression, options);
        }

        public static ValueTask<WriterResult> SaveToStreamAsync(
            Image image,
            Stream stream,
            WriterOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return SaveToStreamCoreAsync(image, stream, compression: null, options, cancellationToken);
        }

        public static ValueTask<WriterResult> SaveToStreamAsync(
            Image image,
            Stream stream,
            Compression compression,
            WriterOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ModelValidation.ValidateEnum(compression, nameof(compression));
            return SaveToStreamCoreAsync(image, stream, compression, options, cancellationToken);
        }

        public static WriterResult SaveToFile(
            Image image,
            string path,
            WriterOptions? options = null)
        {
            return SaveToFileCore(image, path, compression: null, options);
        }

        public static WriterResult SaveToFile(
            Image image,
            string path,
            Compression compression,
            WriterOptions? options = null)
        {
            ModelValidation.ValidateEnum(compression, nameof(compression));
            return SaveToFileCore(image, path, compression, options);
        }

        public static ValueTask<WriterResult> SaveToFileAsync(
            Image image,
            string path,
            WriterOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return SaveToFileCoreAsync(image, path, compression: null, options, cancellationToken);
        }

        public static ValueTask<WriterResult> SaveToFileAsync(
            Image image,
            string path,
            Compression compression,
            WriterOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ModelValidation.ValidateEnum(compression, nameof(compression));
            return SaveToFileCoreAsync(image, path, compression, options, cancellationToken);
        }

        private static ReaderResult<Image> ReadImage(ExrReader reader)
        {
            ReaderResult parse = reader.ParseHeader();
            if (!parse.IsSuccess)
            {
                return new ReaderResult<Image>(parse, null);
            }

            Part[] parts = new Part[reader.NumParts];
            long bytesWritten = 0;
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                ReaderResult<Part> part = reader.ReadPart(partIndex);
                if (!part.IsSuccess || part.Value == null)
                {
                    return new ReaderResult<Image>(part.Operation, null);
                }

                parts[partIndex] = part.Value;
                bytesWritten = checked(bytesWritten + part.BytesWritten);
            }

            return new ReaderResult<Image>(
                new ReaderResult(ExrResult.Success, null, null, bytesWritten),
                new Image(parts));
        }

        private static async ValueTask<ReaderResult<Image>> ReadImageAsync(
            ExrReader reader,
            CancellationToken cancellationToken)
        {
            ReaderResult parse = await reader.ParseHeaderAsync(cancellationToken).ConfigureAwait(false);
            if (!parse.IsSuccess)
            {
                return new ReaderResult<Image>(parse, null);
            }

            Part[] parts = new Part[reader.NumParts];
            long bytesWritten = 0;
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                ReaderResult<Part> part = await reader.ReadPartAsync(
                    partIndex,
                    cancellationToken).ConfigureAwait(false);
                if (!part.IsSuccess || part.Value == null)
                {
                    return new ReaderResult<Image>(part.Operation, null);
                }

                parts[partIndex] = part.Value;
                bytesWritten = checked(bytesWritten + part.BytesWritten);
            }

            return new ReaderResult<Image>(
                new ReaderResult(ExrResult.Success, null, null, bytesWritten),
                new Image(parts));
        }

        private static WriterResult<byte[]> SaveToMemoryCore(
            Image image,
            Compression? compression,
            WriterOptions? options)
        {
            using MemoryStream stream = new MemoryStream();
            WriterOptions effectiveOptions = options ?? new WriterOptions();
            WriterResult result = SaveToStreamCore(
                image,
                stream,
                compression,
                new WriterOptions(
                    effectiveOptions.Limits,
                    leaveOpen: true,
                    forceMultipart: effectiveOptions.ForceMultipart));
            return new WriterResult<byte[]>(result, result.IsSuccess ? stream.ToArray() : null);
        }

        private static WriterResult SaveToStreamCore(
            Image image,
            Stream stream,
            Compression? compression,
            WriterOptions? options)
        {
            ValidateSaveArguments(image, stream);
            WriterOptions effectiveOptions = options ?? new WriterOptions();
            long origin = stream.Position;
            using StreamDataSink sink = new StreamDataSink(
                stream,
                origin,
                leaveOpen: effectiveOptions.LeaveOpen);
            using ExrWriter writer = ExrWriter.OpenSink(
                sink,
                new WriterOptions(
                    effectiveOptions.Limits,
                    leaveOpen: true,
                    forceMultipart: effectiveOptions.ForceMultipart));
            return WriteImage(writer, image, compression);
        }

        private static async ValueTask<WriterResult> SaveToStreamCoreAsync(
            Image image,
            Stream stream,
            Compression? compression,
            WriterOptions? options,
            CancellationToken cancellationToken)
        {
            ValidateSaveArguments(image, stream);
            WriterOptions effectiveOptions = options ?? new WriterOptions();
            long origin = stream.Position;
            await using StreamDataSink sink = new StreamDataSink(
                stream,
                origin,
                leaveOpen: effectiveOptions.LeaveOpen);
            await using ExrWriter writer = ExrWriter.OpenAsyncSink(
                sink,
                new WriterOptions(
                    effectiveOptions.Limits,
                    leaveOpen: true,
                    forceMultipart: effectiveOptions.ForceMultipart));
            return await WriteImageAsync(writer, image, compression, cancellationToken).ConfigureAwait(false);
        }

        private static WriterResult SaveToFileCore(
            Image image,
            string path,
            Compression? compression,
            WriterOptions? options)
        {
            ValidatePath(path);
            try
            {
                using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                WriterOptions effectiveOptions = options ?? new WriterOptions();
                return SaveToStreamCore(
                    image,
                    stream,
                    compression,
                    new WriterOptions(
                        effectiveOptions.Limits,
                        leaveOpen: false,
                        forceMultipart: effectiveOptions.ForceMultipart));
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return WriterFailure(ExrResult.IO, exception);
            }
        }

        private static async ValueTask<WriterResult> SaveToFileCoreAsync(
            Image image,
            string path,
            Compression? compression,
            WriterOptions? options,
            CancellationToken cancellationToken)
        {
            ValidatePath(path);
            try
            {
                using FileStream stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true);
                WriterOptions effectiveOptions = options ?? new WriterOptions();
                return await SaveToStreamCoreAsync(
                    image,
                    stream,
                    compression,
                    new WriterOptions(
                        effectiveOptions.Limits,
                        leaveOpen: false,
                        forceMultipart: effectiveOptions.ForceMultipart),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsIoException(exception))
            {
                return WriterFailure(ExrResult.IO, exception);
            }
        }

        private static WriterResult WriteImage(
            ExrWriter writer,
            Image image,
            Compression? compression)
        {
            try
            {
                Header[] headers = AddParts(writer, image, compression);
                WriterResult result = writer.Begin();
                if (!result.IsSuccess)
                {
                    return result;
                }

                for (int partIndex = 0; partIndex < image.Parts.Count; partIndex++)
                {
                    Part part = image.Parts[partIndex];
                    Header header = headers[partIndex];
                    for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(partIndex); blockIndex++)
                    {
                        BlockInfo info = writer.GetBlockInfo(partIndex, blockIndex);
                        result = WriteBlock(writer, partIndex, part, header, info);
                        if (!result.IsSuccess)
                        {
                            return result;
                        }
                    }
                }

                return writer.End();
            }
            catch (Exception exception) when (IsModelException(exception))
            {
                return WriterFailure(MapModelException(exception), exception);
            }
        }

        private static async ValueTask<WriterResult> WriteImageAsync(
            ExrWriter writer,
            Image image,
            Compression? compression,
            CancellationToken cancellationToken)
        {
            try
            {
                Header[] headers = AddParts(writer, image, compression);
                WriterResult result = await writer.BeginAsync(cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    return result;
                }

                for (int partIndex = 0; partIndex < image.Parts.Count; partIndex++)
                {
                    Part part = image.Parts[partIndex];
                    Header header = headers[partIndex];
                    for (int blockIndex = 0; blockIndex < writer.GetNumBlocks(partIndex); blockIndex++)
                    {
                        BlockInfo info = writer.GetBlockInfo(partIndex, blockIndex);
                        result = await WriteBlockAsync(
                            writer,
                            partIndex,
                            part,
                            header,
                            info,
                            cancellationToken).ConfigureAwait(false);
                        if (!result.IsSuccess)
                        {
                            return result;
                        }
                    }
                }

                return await writer.EndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsModelException(exception))
            {
                return WriterFailure(MapModelException(exception), exception);
            }
        }

        private static Header[] AddParts(ExrWriter writer, Image image, Compression? compression)
        {
            Header[] headers = new Header[image.Parts.Count];
            for (int partIndex = 0; partIndex < image.Parts.Count; partIndex++)
            {
                Part part = image.Parts[partIndex];
                if (!part.IsComplete)
                {
                    throw new ArgumentException("High-level save requires every image part to be complete.", nameof(image));
                }

                Header header = compression.HasValue
                    ? WithCompression(part.Header, compression.Value)
                    : part.Header;
                headers[partIndex] = header;
                writer.AddPart(header);
            }

            return headers;
        }

        private static Header WithCompression(Header header, Compression compression)
        {
            return new Header(
                header.PartType,
                header.DataWindow,
                header.Channels,
                compression,
                header.LineOrder,
                header.DisplayWindow,
                header.PixelAspectRatio,
                header.ScreenWindowCenter,
                header.ScreenWindowWidth,
                header.Tiles,
                header.Name,
                header.Chromaticities,
                header.Attributes);
        }

        private static WriterResult WriteBlock(
            ExrWriter writer,
            int partIndex,
            Part part,
            Header header,
            BlockInfo info)
        {
            PartLevel level = part.GetLevel(info.LevelX, info.LevelY);
            if (header.IsDeep)
            {
                DeepLevel deep = (DeepLevel)level;
                ExtractDeepBlock(deep, header, info.Region, out int[] counts, out ChannelBuffer[] channels);
                return info.IsTiled
                    ? writer.WriteDeepTile(
                        partIndex,
                        info.TileX,
                        info.TileY,
                        info.LevelX,
                        info.LevelY,
                        counts,
                        channels)
                    : writer.WriteDeepScanlineBlock(partIndex, info.Region.MinY, counts, channels);
            }

            ChannelBuffer[] flatChannels = ExtractFlatBlock((FlatLevel)level, header, info.Region);
            return info.IsTiled
                ? writer.WriteTile(
                    partIndex,
                    info.TileX,
                    info.TileY,
                    info.LevelX,
                    info.LevelY,
                    flatChannels)
                : writer.WriteScanlineBlock(partIndex, info.Region.MinY, flatChannels);
        }

        private static async ValueTask<WriterResult> WriteBlockAsync(
            ExrWriter writer,
            int partIndex,
            Part part,
            Header header,
            BlockInfo info,
            CancellationToken cancellationToken)
        {
            PartLevel level = part.GetLevel(info.LevelX, info.LevelY);
            if (header.IsDeep)
            {
                DeepLevel deep = (DeepLevel)level;
                ExtractDeepBlock(deep, header, info.Region, out int[] counts, out ChannelBuffer[] channels);
                return info.IsTiled
                    ? await writer.WriteDeepTileAsync(
                        partIndex,
                        info.TileX,
                        info.TileY,
                        info.LevelX,
                        info.LevelY,
                        counts,
                        channels,
                        cancellationToken).ConfigureAwait(false)
                    : await writer.WriteDeepScanlineBlockAsync(
                        partIndex,
                        info.Region.MinY,
                        counts,
                        channels,
                        cancellationToken).ConfigureAwait(false);
            }

            ChannelBuffer[] flatChannels = ExtractFlatBlock((FlatLevel)level, header, info.Region);
            return info.IsTiled
                ? await writer.WriteTileAsync(
                    partIndex,
                    info.TileX,
                    info.TileY,
                    info.LevelX,
                    info.LevelY,
                    flatChannels,
                    cancellationToken).ConfigureAwait(false)
                : await writer.WriteScanlineBlockAsync(
                    partIndex,
                    info.Region.MinY,
                    flatChannels,
                    cancellationToken).ConfigureAwait(false);
        }

        private static ChannelBuffer[] ExtractFlatBlock(
            FlatLevel level,
            Header header,
            Box2i region)
        {
            ChannelBuffer[] result = new ChannelBuffer[header.Channels.Count];
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                Channel channel = header.Channels[channelIndex];
                ChannelBuffer source = level.GetChannel(channel.Name);
                int elementSize = ModelValidation.PixelTypeSize(channel.PixelType);
                int sourceRowSamples = checked((int)ModelValidation.CountSampleLocations(
                    level.Region.MinX,
                    level.Region.MaxX,
                    channel.XSampling));
                int blockRowSamples = checked((int)ModelValidation.CountSampleLocations(
                    region.MinX,
                    region.MaxX,
                    channel.XSampling));
                int blockRows = checked((int)ModelValidation.CountSampleLocations(
                    region.MinY,
                    region.MaxY,
                    channel.YSampling));
                byte[] destination = new byte[checked(blockRowSamples * blockRows * elementSize)];
                int firstColumn = CountSampleLocationsBefore(
                    level.Region.MinX,
                    region.MinX,
                    channel.XSampling);
                int destinationOffset = 0;
                for (int y = region.MinY; y <= region.MaxY; y++)
                {
                    if (y % channel.YSampling != 0)
                    {
                        continue;
                    }

                    int sourceRow = checked((int)ModelValidation.CountSampleLocations(
                        level.Region.MinY,
                        y,
                        channel.YSampling) - 1);
                    int sourceOffset = checked(
                        ((sourceRow * sourceRowSamples) + firstColumn) * elementSize);
                    int rowBytes = checked(blockRowSamples * elementSize);
                    source.Data.Slice(sourceOffset, rowBytes).CopyTo(
                        destination.AsSpan(destinationOffset, rowBytes));
                    destinationOffset += rowBytes;
                }

                if (destinationOffset != destination.Length)
                {
                    throw new InvalidOperationException("The flat block extraction produced an inconsistent byte count.");
                }

                result[channelIndex] = ChannelBuffer.Adopt(channel.Name, channel.PixelType, destination);
            }

            return result;
        }

        private static int CountSampleLocationsBefore(int minimum, int value, int sampling)
        {
            if (value <= minimum)
            {
                return 0;
            }

            return checked((int)ModelValidation.CountSampleLocations(minimum, value - 1, sampling));
        }

        private static void ExtractDeepBlock(
            DeepLevel level,
            Header header,
            Box2i region,
            out int[] counts,
            out ChannelBuffer[] channels)
        {
            int levelWidth = checked((int)level.Region.Width);
            int blockWidth = checked((int)region.Width);
            int blockHeight = checked((int)region.Height);
            counts = new int[checked(blockWidth * blockHeight)];
            int countOffset = 0;
            ulong totalSamples = 0;
            for (int y = region.MinY; y <= region.MaxY; y++)
            {
                int sourcePixel = checked(
                    ((y - level.Region.MinY) * levelWidth) +
                    (region.MinX - level.Region.MinX));
                level.SampleCounts.Slice(sourcePixel, blockWidth).CopyTo(
                    counts.AsSpan(countOffset, blockWidth));
                for (int x = 0; x < blockWidth; x++)
                {
                    totalSamples = checked(totalSamples + (uint)counts[countOffset + x]);
                }

                countOffset += blockWidth;
            }

            channels = new ChannelBuffer[header.Channels.Count];
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                Channel channel = header.Channels[channelIndex];
                int elementSize = ModelValidation.PixelTypeSize(channel.PixelType);
                ulong byteCount = checked(totalSamples * (uint)elementSize);
                if (byteCount > ChannelBuffer.MaxMaterializedByteLength)
                {
                    throw new NotSupportedException("A deep block channel exceeds the managed buffer address space.");
                }

                byte[] destination = new byte[(int)byteCount];
                int destinationOffset = 0;
                for (int y = region.MinY; y <= region.MaxY; y++)
                {
                    int sourcePixel = checked(
                        ((y - level.Region.MinY) * levelWidth) +
                        (region.MinX - level.Region.MinX));
                    for (int x = 0; x < blockWidth; x++)
                    {
                        ReadOnlySpan<byte> samples = level.GetSamples(channel.Name, sourcePixel + x);
                        samples.CopyTo(destination.AsSpan(destinationOffset));
                        destinationOffset += samples.Length;
                    }
                }

                if (destinationOffset != destination.Length)
                {
                    throw new InvalidOperationException(
                        $"Deep channel '{channel.Name}' does not contain one sample range per block count.");
                }

                channels[channelIndex] = new ChannelBuffer(channel.Name, channel.PixelType, destination);
            }
        }

        private static void ValidateSaveArguments(Image image, Stream stream)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
        }

        private static void ValidatePath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (path.Length == 0)
            {
                throw new ArgumentException("The path must not be empty.", nameof(path));
            }
        }

        private static bool IsIoException(Exception exception)
        {
            return exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException;
        }

        private static bool IsModelException(Exception exception)
        {
            return exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is OverflowException ||
                exception is OutOfMemoryException ||
                exception is WriterLimitExceededException;
        }

        private static ExrResult MapModelException(Exception exception)
        {
            if (exception is OutOfMemoryException)
            {
                return ExrResult.OutOfMemory;
            }

            if (exception is NotSupportedException ||
                exception is OverflowException ||
                exception is WriterLimitExceededException)
            {
                return ExrResult.Unsupported;
            }

            return ExrResult.InvalidArgument;
        }

        private static ReaderResult<T> ReaderFailure<T>(ExrResult result, Exception exception)
            where T : class
        {
            return new ReaderResult<T>(new ReaderResult(result, null, exception), null);
        }

        private static WriterResult WriterFailure(ExrResult result, Exception exception)
        {
            return new WriterResult(result, null, exception);
        }
    }
}
