using System;
using System.Collections.Generic;
using System.IO;
using TinyEXR.V3.Format;

namespace TinyEXR.V3
{
    internal sealed class DeepReadOperation
    {
        private readonly ReaderPartData _part;
        private readonly ReaderLimits _limits;
        private readonly DeepReadTarget[] _targets;
        private readonly DeepReadBlock[] _blocks;
        private int[] _countDestination = Array.Empty<int>();
        private int _countBlockIndex;
        private int _sampleBlockIndex;
        private bool _materialized;
        private long _materializedByteCount;

        private DeepReadOperation(
            FlatReadRequest request,
            ReaderPartData part,
            ReaderLimits limits,
            DeepReadTarget[] targets,
            DeepReadBlock[] blocks,
            bool isCompletePart,
            long countByteCount)
        {
            Request = request;
            _part = part;
            _limits = limits;
            _targets = targets;
            _blocks = blocks;
            IsCompletePart = isCompletePart;
            _materializedByteCount = countByteCount;
        }

        public FlatReadRequest Request { get; }

        public bool IsCompletePart { get; }

        public long MaterializedByteCount => _materializedByteCount;

        public bool CountsComplete => _countBlockIndex == _blocks.Length;

        public bool IsComplete => CountsComplete && _sampleBlockIndex == _blocks.Length;

        public int CurrentCountBlockIndex => !CountsComplete
            ? _blocks[_countBlockIndex].BlockIndex
            : throw new InvalidOperationException("The deep count pass is complete.");

        public int CurrentSampleBlockIndex => CountsComplete && !IsComplete
            ? _blocks[_sampleBlockIndex].BlockIndex
            : throw new InvalidOperationException("The deep sample pass is not active.");

        public static DeepReadOperation Create(
            FlatReadRequest request,
            ReaderPartData part,
            ReaderLimits limits)
        {
            if (!part.Header.IsDeep)
            {
                throw new NotSupportedException("Flat parts require the flat materialization path.");
            }

            List<TargetDescriptor> descriptors = new List<TargetDescriptor>();
            bool isCompletePart;
            switch (request.Kind)
            {
                case FlatReadKind.Part:
                    for (int levelIndex = 0; levelIndex < part.Layout.LevelCount; levelIndex++)
                    {
                        ReaderLevelInfo level = part.Layout.GetLevelInfo(levelIndex);
                        descriptors.Add(new TargetDescriptor(
                            level.LevelX,
                            level.LevelY,
                            level.Region,
                            level.FirstBlockIndex,
                            level.BlockCount));
                    }

                    isCompletePart = true;
                    break;
                case FlatReadKind.Scanlines:
                    if (part.Header.PartType != PartType.DeepScanline)
                    {
                        throw new ArgumentException(
                            "ReadScanlines requires a deep scanline part.",
                            nameof(request));
                    }

                    int minimumY = request.Value0;
                    int lineCount = request.Value1;
                    if (lineCount <= 0)
                    {
                        throw new ArgumentOutOfRangeException("lineCount");
                    }

                    long maximumY = checked((long)minimumY + lineCount - 1L);
                    if (minimumY < part.Header.DataWindow.MinY ||
                        maximumY > part.Header.DataWindow.MaxY)
                    {
                        throw new ArgumentOutOfRangeException(
                            "minimumY",
                            "The requested scanline range lies outside the data window.");
                    }

                    int linesPerBlock = ExrFormatParser.LinesPerBlock(part.Header.Compression);
                    int firstBlockIndex = checked(
                        (minimumY - part.Header.DataWindow.MinY) / linesPerBlock);
                    int lastBlockIndex = checked(
                        ((int)maximumY - part.Header.DataWindow.MinY) / linesPerBlock);
                    descriptors.Add(new TargetDescriptor(
                        0,
                        0,
                        new Box2i(
                            part.Header.DataWindow.MinX,
                            minimumY,
                            part.Header.DataWindow.MaxX,
                            (int)maximumY),
                        firstBlockIndex,
                        checked(lastBlockIndex - firstBlockIndex + 1)));
                    isCompletePart = minimumY == part.Header.DataWindow.MinY &&
                        maximumY == part.Header.DataWindow.MaxY;
                    break;
                case FlatReadKind.Tile:
                    if (part.Header.PartType != PartType.DeepTiled)
                    {
                        throw new ArgumentException(
                            "ReadTile requires a deep tiled part.",
                            nameof(request));
                    }

                    if (!part.Layout.TryGetTiledBlockIndex(
                        request.Value0,
                        request.Value1,
                        request.Value2,
                        request.Value3,
                        out int tileBlockIndex))
                    {
                        throw new ArgumentOutOfRangeException(
                            "tileX",
                            "The requested tile or level lies outside the tiled part.");
                    }

                    BlockInfo tileInfo = part.Layout.GetBlockInfo(
                        part.PartIndex,
                        tileBlockIndex,
                        fileOffset: 0);
                    descriptors.Add(new TargetDescriptor(
                        tileInfo.LevelX,
                        tileInfo.LevelY,
                        tileInfo.Region,
                        tileBlockIndex,
                        1));
                    isCompletePart = part.Layout.BlockCount == 1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }

            long countByteCount = 0;
            int totalBlockCount = 0;
            for (int targetIndex = 0; targetIndex < descriptors.Count; targetIndex++)
            {
                TargetDescriptor descriptor = descriptors[targetIndex];
                ulong pixelCount = ModelValidation.CountPixels(descriptor.Region);
                if (pixelCount > int.MaxValue)
                {
                    throw new ReaderLimitExceededException(
                        nameof(ChannelBuffer.MaxMaterializedByteLength),
                        pixelCount <= long.MaxValue ? (long)pixelCount : long.MaxValue,
                        int.MaxValue);
                }

                countByteCount = checked(countByteCount + checked((long)pixelCount * sizeof(int)));
                if (countByteCount > limits.MaximumMaterializedByteCount)
                {
                    throw new ReaderLimitExceededException(
                        nameof(ReaderLimits.MaximumMaterializedByteCount),
                        countByteCount,
                        limits.MaximumMaterializedByteCount);
                }

                totalBlockCount = checked(totalBlockCount + descriptor.BlockCount);
            }

            DeepReadTarget[] targets = new DeepReadTarget[descriptors.Count];
            DeepReadBlock[] blocks = new DeepReadBlock[totalBlockCount];
            int nextBlock = 0;
            for (int targetIndex = 0; targetIndex < descriptors.Count; targetIndex++)
            {
                TargetDescriptor descriptor = descriptors[targetIndex];
                int pixelCount = checked((int)ModelValidation.CountPixels(descriptor.Region));
                targets[targetIndex] = new DeepReadTarget(descriptor, new int[pixelCount]);
                for (int blockOffset = 0; blockOffset < descriptor.BlockCount; blockOffset++)
                {
                    blocks[nextBlock++] = new DeepReadBlock(
                        targetIndex,
                        checked(descriptor.FirstBlockIndex + blockOffset));
                }
            }

            return new DeepReadOperation(
                request,
                part,
                limits,
                targets,
                blocks,
                isCompletePart,
                countByteCount);
        }

        public Memory<int> GetCountDestination()
        {
            BlockInfo info = _part.Layout.GetBlockInfo(
                _part.PartIndex,
                CurrentCountBlockIndex,
                fileOffset: 0);
            int required = checked((int)checked(info.Region.Width * info.Region.Height));
            if (_countDestination.Length < required)
            {
                _countDestination = new int[required];
            }

            return _countDestination.AsMemory(0, required);
        }

        public ReaderResult? AcceptDecodedCounts(DeepBlockOperation operation)
        {
            if (CountsComplete ||
                operation.Info.PartIndex != _part.PartIndex ||
                operation.Info.BlockIndex != CurrentCountBlockIndex ||
                !operation.CountsDecoded)
            {
                throw new InvalidOperationException("The completed deep count block does not match the read operation.");
            }

            DeepReadBlock block = _blocks[_countBlockIndex];
            ReaderResult? scatter = ScatterCounts(
                operation.Info.Region,
                operation.DecodedCounts,
                _targets[block.TargetIndex]);
            if (scatter.HasValue)
            {
                return scatter.Value;
            }

            block.Operation = operation;
            _countBlockIndex++;
            if (!CountsComplete)
            {
                return null;
            }

            return AllocateTargetChannels();
        }

        public void GetSampleArguments(
            out DeepBlockOperation operation,
            out ReadOnlyMemory<int> counts,
            out IReadOnlyList<DeepChannelDestination> destinations)
        {
            if (!CountsComplete || IsComplete)
            {
                throw new InvalidOperationException("The deep sample pass is not active.");
            }

            DeepReadBlock block = _blocks[_sampleBlockIndex];
            operation = block.Operation ??
                throw new InvalidOperationException("The deep block count state was not retained.");
            counts = operation.DecodedCountMemory;
            if (block.SampleChannels == null)
            {
                byte[][] sampleChannels = new byte[_part.Header.Channels.Count][];
                DeepChannelDestination[] blockDestinations =
                    new DeepChannelDestination[_part.Header.Channels.Count];
                for (int channelIndex = 0; channelIndex < sampleChannels.Length; channelIndex++)
                {
                    Channel channel = _part.Header.Channels[channelIndex];
                    int byteCount = checked((int)checked(
                        operation.TotalSamples * ModelValidation.PixelTypeSize(channel.PixelType)));
                    byte[] data = new byte[byteCount];
                    sampleChannels[channelIndex] = data;
                    blockDestinations[channelIndex] = new DeepChannelDestination(channel.Name, data);
                }

                block.SampleChannels = sampleChannels;
                block.Destinations = blockDestinations;
            }

            destinations = block.Destinations!;
        }

        public ReaderResult? AcceptDecodedSamples()
        {
            if (!CountsComplete || IsComplete)
            {
                throw new InvalidOperationException("The deep sample pass is not active.");
            }

            DeepReadBlock block = _blocks[_sampleBlockIndex];
            DeepBlockOperation operation = block.Operation ??
                throw new InvalidOperationException("The deep block count state was not retained.");
            ReaderResult? scatter = ScatterSamples(
                operation.Info.Region,
                operation.DecodedCounts,
                block.SampleChannels ?? throw new InvalidOperationException("The deep block samples are unavailable."),
                _targets[block.TargetIndex]);
            if (scatter.HasValue)
            {
                return scatter.Value;
            }

            block.Operation = null;
            block.SampleChannels = null;
            block.Destinations = null;
            _sampleBlockIndex++;
            return null;
        }

        public Part BuildPart()
        {
            if (!IsComplete || _materialized)
            {
                throw new InvalidOperationException("The deep materialization is not ready to publish.");
            }

            List<PartLevel> levels = new List<PartLevel>(_targets.Length);
            for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            {
                DeepReadTarget target = _targets[targetIndex];
                byte[][] targetChannels = target.Channels ??
                    throw new InvalidOperationException("The deep target channel buffers were not allocated.");
                List<ChannelBuffer> channels = new List<ChannelBuffer>(targetChannels.Length);
                for (int channelIndex = 0; channelIndex < targetChannels.Length; channelIndex++)
                {
                    Channel channel = _part.Header.Channels[channelIndex];
                    channels.Add(ChannelBuffer.Adopt(
                        channel.Name,
                        channel.PixelType,
                        targetChannels[channelIndex]));
                }

                levels.Add(DeepLevel.Adopt(
                    target.LevelX,
                    target.LevelY,
                    target.Region,
                    target.Counts,
                    _part.Header.Channels,
                    channels));
            }

            Part result = new Part(_part.Header, levels, IsCompletePart);
            _materialized = true;
            return result;
        }

        private ReaderResult? AllocateTargetChannels()
        {
            long materializedByteCount = _materializedByteCount;
            long aggregateSamples = 0;
            int[][] channelByteCounts = new int[_targets.Length][];
            for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            {
                DeepReadTarget target = _targets[targetIndex];
                ulong[] sampleOffsets = new ulong[target.Counts.Length];
                ulong totalSamples = 0;
                for (int pixelIndex = 0; pixelIndex < target.Counts.Length; pixelIndex++)
                {
                    sampleOffsets[pixelIndex] = totalSamples;
                    totalSamples = checked(totalSamples + (uint)target.Counts[pixelIndex]);
                }

                target.SampleOffsets = sampleOffsets;
                target.TotalSamples = totalSamples;
                aggregateSamples = checked(aggregateSamples + checked((long)totalSamples));
                if (aggregateSamples > _limits.MaximumDeepSampleCount)
                {
                    return Limit(
                        nameof(ReaderLimits.MaximumDeepSampleCount),
                        aggregateSamples,
                        _limits.MaximumDeepSampleCount);
                }

                int[] targetByteCounts = new int[_part.Header.Channels.Count];
                channelByteCounts[targetIndex] = targetByteCounts;
                for (int channelIndex = 0; channelIndex < targetByteCounts.Length; channelIndex++)
                {
                    Channel channel = _part.Header.Channels[channelIndex];
                    ulong byteCount = checked(
                        totalSamples * (uint)ModelValidation.PixelTypeSize(channel.PixelType));
                    if (byteCount > ChannelBuffer.MaxMaterializedByteLength)
                    {
                        return Limit(
                            nameof(ChannelBuffer.MaxMaterializedByteLength),
                            byteCount <= long.MaxValue ? (long)byteCount : long.MaxValue,
                            ChannelBuffer.MaxMaterializedByteLength);
                    }

                    targetByteCounts[channelIndex] = (int)byteCount;
                    materializedByteCount = checked(materializedByteCount + (long)byteCount);
                    if (materializedByteCount > _limits.MaximumMaterializedByteCount)
                    {
                        return Limit(
                            nameof(ReaderLimits.MaximumMaterializedByteCount),
                            materializedByteCount,
                            _limits.MaximumMaterializedByteCount);
                    }
                }
            }

            for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            {
                int[] targetByteCounts = channelByteCounts[targetIndex];
                byte[][] channels = new byte[targetByteCounts.Length][];
                for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    channels[channelIndex] = new byte[targetByteCounts[channelIndex]];
                }

                _targets[targetIndex].Channels = channels;
            }

            _materializedByteCount = materializedByteCount;
            return null;
        }

        private static ReaderResult? ScatterCounts(
            Box2i blockRegion,
            ReadOnlySpan<int> source,
            DeepReadTarget target)
        {
            try
            {
                int blockWidth = checked((int)blockRegion.Width);
                int blockHeight = checked((int)blockRegion.Height);
                if (source.Length != checked(blockWidth * blockHeight))
                {
                    return Corrupt("A decoded deep count block has an inconsistent pixel count.");
                }

                int overlapMinimumX = Math.Max(blockRegion.MinX, target.Region.MinX);
                int overlapMaximumX = Math.Min(blockRegion.MaxX, target.Region.MaxX);
                int overlapMinimumY = Math.Max(blockRegion.MinY, target.Region.MinY);
                int overlapMaximumY = Math.Min(blockRegion.MaxY, target.Region.MaxY);
                if (overlapMinimumX > overlapMaximumX || overlapMinimumY > overlapMaximumY)
                {
                    return Corrupt("A selected deep block does not overlap its target region.");
                }

                int targetWidth = checked((int)target.Region.Width);
                int copyCount = checked(overlapMaximumX - overlapMinimumX + 1);
                for (int y = overlapMinimumY; y <= overlapMaximumY; y++)
                {
                    int sourceOffset = checked(
                        checked(y - blockRegion.MinY) * blockWidth +
                        checked(overlapMinimumX - blockRegion.MinX));
                    int targetOffset = checked(
                        checked(y - target.Region.MinY) * targetWidth +
                        checked(overlapMinimumX - target.Region.MinX));
                    source.Slice(sourceOffset, copyCount).CopyTo(
                        target.Counts.AsSpan(targetOffset, copyCount));
                }

                return null;
            }
            catch (Exception exception) when (
                exception is OverflowException ||
                exception is ArgumentException)
            {
                return new ReaderResult(ExrResult.Corrupt, null, exception);
            }
        }

        private ReaderResult? ScatterSamples(
            Box2i blockRegion,
            ReadOnlySpan<int> sourceCounts,
            byte[][] sourceChannels,
            DeepReadTarget target)
        {
            try
            {
                byte[][] targetChannels = target.Channels ??
                    throw new InvalidOperationException("The deep target channel buffers were not allocated.");
                ulong[] targetOffsets = target.SampleOffsets ??
                    throw new InvalidOperationException("The deep target sample offsets were not built.");
                int targetWidth = checked((int)target.Region.Width);
                int expectedPixels = checked((int)checked(blockRegion.Width * blockRegion.Height));
                if (sourceCounts.Length != expectedPixels ||
                    sourceChannels.Length != _part.Header.Channels.Count ||
                    targetChannels.Length != _part.Header.Channels.Count)
                {
                    return Corrupt("A decoded deep sample block has an inconsistent layout.");
                }

                ulong sourceSampleOffset = 0;
                int sourcePixel = 0;
                for (int y = blockRegion.MinY; y <= blockRegion.MaxY; y++)
                {
                    for (int x = blockRegion.MinX; x <= blockRegion.MaxX; x++)
                    {
                        int count = sourceCounts[sourcePixel++];
                        if (x >= target.Region.MinX && x <= target.Region.MaxX &&
                            y >= target.Region.MinY && y <= target.Region.MaxY)
                        {
                            int targetPixel = checked(
                                checked(y - target.Region.MinY) * targetWidth +
                                checked(x - target.Region.MinX));
                            if (target.Counts[targetPixel] != count)
                            {
                                return Corrupt("Deep count and sample passes disagree for a target pixel.");
                            }

                            ulong targetSampleOffset = targetOffsets[targetPixel];
                            for (int channelIndex = 0; channelIndex < sourceChannels.Length; channelIndex++)
                            {
                                int elementSize = ModelValidation.PixelTypeSize(
                                    _part.Header.Channels[channelIndex].PixelType);
                                int byteCount = checked(count * elementSize);
                                int sourceByteOffset = checked((int)checked(sourceSampleOffset * (uint)elementSize));
                                int targetByteOffset = checked((int)checked(targetSampleOffset * (uint)elementSize));
                                sourceChannels[channelIndex].AsSpan(sourceByteOffset, byteCount).CopyTo(
                                    targetChannels[channelIndex].AsSpan(targetByteOffset, byteCount));
                            }
                        }

                        sourceSampleOffset = checked(sourceSampleOffset + (uint)count);
                    }
                }

                for (int channelIndex = 0; channelIndex < sourceChannels.Length; channelIndex++)
                {
                    int expectedByteCount = checked((int)checked(
                        sourceSampleOffset *
                        (uint)ModelValidation.PixelTypeSize(_part.Header.Channels[channelIndex].PixelType)));
                    if (sourceChannels[channelIndex].Length != expectedByteCount)
                    {
                        return Corrupt("A decoded deep channel has an inconsistent sample byte count.");
                    }
                }

                return null;
            }
            catch (Exception exception) when (
                exception is OverflowException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                return new ReaderResult(ExrResult.Corrupt, null, exception);
            }
        }

        private static ReaderResult Corrupt(string message)
        {
            return new ReaderResult(
                ExrResult.Corrupt,
                null,
                new InvalidDataException(message));
        }

        private static ReaderResult Limit(string name, long actual, long maximum)
        {
            return new ReaderResult(
                ExrResult.Unsupported,
                null,
                new ReaderLimitExceededException(name, actual, maximum));
        }

        private readonly struct TargetDescriptor
        {
            public TargetDescriptor(
                int levelX,
                int levelY,
                Box2i region,
                int firstBlockIndex,
                int blockCount)
            {
                LevelX = levelX;
                LevelY = levelY;
                Region = region;
                FirstBlockIndex = firstBlockIndex;
                BlockCount = blockCount;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public Box2i Region { get; }

            public int FirstBlockIndex { get; }

            public int BlockCount { get; }
        }

        private sealed class DeepReadTarget
        {
            public DeepReadTarget(TargetDescriptor descriptor, int[] counts)
            {
                LevelX = descriptor.LevelX;
                LevelY = descriptor.LevelY;
                Region = descriptor.Region;
                Counts = counts;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public Box2i Region { get; }

            public int[] Counts { get; }

            public ulong[]? SampleOffsets { get; set; }

            public ulong TotalSamples { get; set; }

            public byte[][]? Channels { get; set; }
        }

        private sealed class DeepReadBlock
        {
            public DeepReadBlock(int targetIndex, int blockIndex)
            {
                TargetIndex = targetIndex;
                BlockIndex = blockIndex;
            }

            public int TargetIndex { get; }

            public int BlockIndex { get; }

            public DeepBlockOperation? Operation { get; set; }

            public byte[][]? SampleChannels { get; set; }

            public DeepChannelDestination[]? Destinations { get; set; }
        }
    }
}
