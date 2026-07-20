using System;
using System.Collections.Generic;
using System.IO;
using TinyEXR.V3.Format;

namespace TinyEXR.V3
{
    internal enum FlatReadKind
    {
        Part = 0,
        Scanlines = 1,
        Tile = 2,
    }

    internal readonly struct FlatReadRequest : IEquatable<FlatReadRequest>
    {
        private FlatReadRequest(
            FlatReadKind kind,
            int partIndex,
            int value0,
            int value1,
            int value2,
            int value3)
        {
            Kind = kind;
            PartIndex = partIndex;
            Value0 = value0;
            Value1 = value1;
            Value2 = value2;
            Value3 = value3;
        }

        public FlatReadKind Kind { get; }

        public int PartIndex { get; }

        public int Value0 { get; }

        public int Value1 { get; }

        public int Value2 { get; }

        public int Value3 { get; }

        public static FlatReadRequest Part(int partIndex)
        {
            return new FlatReadRequest(FlatReadKind.Part, partIndex, 0, 0, 0, 0);
        }

        public static FlatReadRequest Scanlines(int partIndex, int minimumY, int lineCount)
        {
            return new FlatReadRequest(
                FlatReadKind.Scanlines,
                partIndex,
                minimumY,
                lineCount,
                0,
                0);
        }

        public static FlatReadRequest Tile(
            int partIndex,
            int tileX,
            int tileY,
            int levelX,
            int levelY)
        {
            return new FlatReadRequest(
                FlatReadKind.Tile,
                partIndex,
                tileX,
                tileY,
                levelX,
                levelY);
        }

        public bool Equals(FlatReadRequest other)
        {
            return Kind == other.Kind &&
                PartIndex == other.PartIndex &&
                Value0 == other.Value0 &&
                Value1 == other.Value1 &&
                Value2 == other.Value2 &&
                Value3 == other.Value3;
        }

        public override bool Equals(object? obj)
        {
            return obj is FlatReadRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ PartIndex;
                hash = (hash * 397) ^ Value0;
                hash = (hash * 397) ^ Value1;
                hash = (hash * 397) ^ Value2;
                return (hash * 397) ^ Value3;
            }
        }
    }

    internal sealed class FlatReadOperation
    {
        private readonly ReaderPartData _part;
        private readonly FlatReadTarget[] _targets;
        private byte[] _canonical = Array.Empty<byte>();
        private int _targetIndex;
        private int _targetBlockOffset;
        private bool _materialized;

        private FlatReadOperation(
            FlatReadRequest request,
            ReaderPartData part,
            FlatReadTarget[] targets,
            bool isCompletePart,
            long materializedByteCount)
        {
            Request = request;
            _part = part;
            _targets = targets;
            IsCompletePart = isCompletePart;
            MaterializedByteCount = materializedByteCount;
        }

        public FlatReadRequest Request { get; }

        public bool IsCompletePart { get; }

        public long MaterializedByteCount { get; }

        public bool IsComplete => _targetIndex == _targets.Length;

        public int CurrentBlockIndex
        {
            get
            {
                FlatReadTarget target = CurrentTarget;
                return checked(target.FirstBlockIndex + _targetBlockOffset);
            }
        }

        private FlatReadTarget CurrentTarget => !IsComplete
            ? _targets[_targetIndex]
            : throw new InvalidOperationException("The flat materialization is complete.");

        public static FlatReadOperation Create(
            FlatReadRequest request,
            ReaderPartData part,
            ReaderLimits limits)
        {
            if (part.Header.IsDeep)
            {
                throw new NotSupportedException("Deep parts require the two-stage deep materialization path.");
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
                    if (part.Header.PartType != PartType.Scanline)
                    {
                        throw new ArgumentException(
                            "ReadScanlines requires a flat scanline part.",
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
                    if (part.Header.PartType != PartType.Tiled)
                    {
                        throw new ArgumentException(
                            "ReadTile requires a flat tiled part.",
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

            long materializedByteCount = 0;
            int[][] targetChannelByteCounts = new int[descriptors.Count][];
            for (int targetIndex = 0; targetIndex < descriptors.Count; targetIndex++)
            {
                TargetDescriptor descriptor = descriptors[targetIndex];
                int[] channelByteCounts = new int[part.Header.Channels.Count];
                targetChannelByteCounts[targetIndex] = channelByteCounts;
                for (int channelIndex = 0; channelIndex < channelByteCounts.Length; channelIndex++)
                {
                    Channel channel = part.Header.Channels[channelIndex];
                    ulong sampleCount = ModelValidation.CountSamples(
                        descriptor.Region,
                        channel.XSampling,
                        channel.YSampling);
                    ulong byteCount = checked(
                        sampleCount * (uint)ModelValidation.PixelTypeSize(channel.PixelType));
                    if (byteCount > ChannelBuffer.MaxMaterializedByteLength)
                    {
                        throw new ReaderLimitExceededException(
                            nameof(ChannelBuffer.MaxMaterializedByteLength),
                            byteCount <= long.MaxValue ? (long)byteCount : long.MaxValue,
                            ChannelBuffer.MaxMaterializedByteLength);
                    }

                    channelByteCounts[channelIndex] = (int)byteCount;
                    materializedByteCount = checked(materializedByteCount + (long)byteCount);
                    if (materializedByteCount > limits.MaximumMaterializedByteCount)
                    {
                        throw new ReaderLimitExceededException(
                            nameof(ReaderLimits.MaximumMaterializedByteCount),
                            materializedByteCount,
                            limits.MaximumMaterializedByteCount);
                    }
                }
            }

            FlatReadTarget[] targets = new FlatReadTarget[descriptors.Count];
            for (int targetIndex = 0; targetIndex < descriptors.Count; targetIndex++)
            {
                TargetDescriptor descriptor = descriptors[targetIndex];
                int[] channelByteCounts = targetChannelByteCounts[targetIndex];
                byte[][] channels = new byte[channelByteCounts.Length][];
                for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    channels[channelIndex] = new byte[channelByteCounts[channelIndex]];
                }

                targets[targetIndex] = new FlatReadTarget(descriptor, channels);
            }

            return new FlatReadOperation(
                request,
                part,
                targets,
                isCompletePart,
                materializedByteCount);
        }

        public Memory<byte> GetCanonicalDestination()
        {
            BlockInfo info = _part.Layout.GetBlockInfo(
                _part.PartIndex,
                CurrentBlockIndex,
                fileOffset: 0);
            if (!info.UncompressedByteCount.HasValue || info.UncompressedByteCount.Value > int.MaxValue)
            {
                throw new NotSupportedException("The canonical block exceeds the managed buffer address space.");
            }

            int required = (int)info.UncompressedByteCount.Value;
            if (_canonical.Length < required)
            {
                _canonical = new byte[required];
            }

            return _canonical.AsMemory(0, required);
        }

        public ReaderResult? AcceptDecodedBlock()
        {
            BlockInfo info = _part.Layout.GetBlockInfo(
                _part.PartIndex,
                CurrentBlockIndex,
                fileOffset: 0);
            int canonicalByteCount = checked((int)info.UncompressedByteCount!.Value);
            ReaderResult? scatter = ScatterCanonicalBlock(
                _part.Header,
                info.Region,
                _canonical.AsSpan(0, canonicalByteCount),
                CurrentTarget.Region,
                CurrentTarget.Channels);
            if (scatter.HasValue)
            {
                return scatter.Value;
            }

            _targetBlockOffset++;
            if (_targetBlockOffset == CurrentTarget.BlockCount)
            {
                _targetIndex++;
                _targetBlockOffset = 0;
            }

            return null;
        }

        public Part BuildPart()
        {
            if (!IsComplete || _materialized)
            {
                throw new InvalidOperationException("The flat materialization is not ready to publish.");
            }

            List<PartLevel> levels = new List<PartLevel>(_targets.Length);
            foreach (FlatReadTarget target in _targets)
            {
                List<ChannelBuffer> channels = new List<ChannelBuffer>(_part.Header.Channels.Count);
                for (int channelIndex = 0; channelIndex < target.Channels.Length; channelIndex++)
                {
                    Channel channel = _part.Header.Channels[channelIndex];
                    channels.Add(ChannelBuffer.Adopt(
                        channel.Name,
                        channel.PixelType,
                        target.Channels[channelIndex]));
                }

                levels.Add(new FlatLevel(
                    target.LevelX,
                    target.LevelY,
                    target.Region,
                    channels));
            }

            Part result = new Part(_part.Header, levels, IsCompletePart);
            _materialized = true;
            return result;
        }

        private static ReaderResult? ScatterCanonicalBlock(
            Header header,
            Box2i blockRegion,
            ReadOnlySpan<byte> canonical,
            Box2i targetRegion,
            byte[][] targetChannels)
        {
            try
            {
                int canonicalOffset = 0;
                for (long y = blockRegion.MinY; y <= blockRegion.MaxY; y++)
                {
                    int absoluteY = (int)y;
                    for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
                    {
                        Channel channel = header.Channels[channelIndex];
                        if (absoluteY % channel.YSampling != 0)
                        {
                            continue;
                        }

                        int elementSize = ModelValidation.PixelTypeSize(channel.PixelType);
                        long sourceSampleCount = ModelValidation.CountSampleLocations(
                            blockRegion.MinX,
                            blockRegion.MaxX,
                            channel.XSampling);
                        int sourceByteCount = checked((int)sourceSampleCount * elementSize);
                        if (sourceByteCount > canonical.Length - canonicalOffset)
                        {
                            return Corrupt("A canonical block ends inside a channel row.");
                        }

                        if (absoluteY >= targetRegion.MinY && absoluteY <= targetRegion.MaxY)
                        {
                            int overlapMinimumX = Math.Max(blockRegion.MinX, targetRegion.MinX);
                            int overlapMaximumX = Math.Min(blockRegion.MaxX, targetRegion.MaxX);
                            if (overlapMinimumX <= overlapMaximumX)
                            {
                                long copySampleCount = ModelValidation.CountSampleLocations(
                                    overlapMinimumX,
                                    overlapMaximumX,
                                    channel.XSampling);
                                if (copySampleCount > 0)
                                {
                                    long sourceColumn = overlapMinimumX == blockRegion.MinX
                                        ? 0
                                        : ModelValidation.CountSampleLocations(
                                            blockRegion.MinX,
                                            overlapMinimumX - 1,
                                            channel.XSampling);
                                    long targetColumn = overlapMinimumX == targetRegion.MinX
                                        ? 0
                                        : ModelValidation.CountSampleLocations(
                                            targetRegion.MinX,
                                            overlapMinimumX - 1,
                                            channel.XSampling);
                                    long targetRow = absoluteY == targetRegion.MinY
                                        ? 0
                                        : ModelValidation.CountSampleLocations(
                                            targetRegion.MinY,
                                            absoluteY,
                                            channel.YSampling) - 1L;
                                    long targetWidth = ModelValidation.CountSampleLocations(
                                        targetRegion.MinX,
                                        targetRegion.MaxX,
                                        channel.XSampling);
                                    int copyByteCount = checked((int)copySampleCount * elementSize);
                                    int sourceByteOffset = checked(
                                        canonicalOffset + checked((int)sourceColumn * elementSize));
                                    int targetByteOffset = checked((int)checked(
                                        (targetRow * targetWidth + targetColumn) * elementSize));
                                    canonical.Slice(sourceByteOffset, copyByteCount).CopyTo(
                                        targetChannels[channelIndex].AsSpan(targetByteOffset, copyByteCount));
                                }
                            }
                        }

                        canonicalOffset = checked(canonicalOffset + sourceByteCount);
                    }
                }

                return canonicalOffset == canonical.Length
                    ? null
                    : Corrupt("A canonical block contains trailing channel data.");
            }
            catch (Exception exception) when (
                exception is OverflowException ||
                exception is ArgumentOutOfRangeException ||
                exception is ArgumentException)
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

        private sealed class FlatReadTarget
        {
            public FlatReadTarget(TargetDescriptor descriptor, byte[][] channels)
            {
                LevelX = descriptor.LevelX;
                LevelY = descriptor.LevelY;
                Region = descriptor.Region;
                FirstBlockIndex = descriptor.FirstBlockIndex;
                BlockCount = descriptor.BlockCount;
                Channels = channels;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public Box2i Region { get; }

            public int FirstBlockIndex { get; }

            public int BlockCount { get; }

            public byte[][] Channels { get; }
        }
    }
}
