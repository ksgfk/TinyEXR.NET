using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyEXR.V3
{
    /// <summary>
    /// Owned native byte storage for one planar channel in a materialized region.
    /// </summary>
    public sealed class ChannelBuffer
    {
        public const int MaxMaterializedByteLength = int.MaxValue;

        private readonly byte[] _data;

        public ChannelBuffer(string name, PixelType pixelType, ReadOnlySpan<byte> data)
            : this(name, pixelType, data.ToArray())
        {
        }

        private ChannelBuffer(string name, PixelType pixelType, byte[] data)
        {
            ModelValidation.ValidateName(name, nameof(name), allowEmpty: false);
            ModelValidation.ValidateEnum(pixelType, nameof(pixelType));
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            int elementSize = ModelValidation.PixelTypeSize(pixelType);
            if (data.Length % elementSize != 0)
            {
                throw new ArgumentException("The channel byte length must contain whole native samples.", nameof(data));
            }

            Name = name;
            PixelType = pixelType;
            _data = data;
        }

        /// <summary>
        /// Adopts parser-owned storage without a second copy. The caller must transfer exclusive ownership.
        /// </summary>
        internal static ChannelBuffer Adopt(string name, PixelType pixelType, byte[] data)
        {
            return new ChannelBuffer(name, pixelType, data);
        }

        public string Name { get; }

        public PixelType PixelType { get; }

        public ReadOnlySpan<byte> Data => _data;

        public int ByteLength => _data.Length;

        public long SampleCount => _data.Length / ModelValidation.PixelTypeSize(PixelType);
    }

    /// <summary>
    /// Base geometry and channel storage for one materialized level, scanline range, or tile.
    /// </summary>
    public abstract class PartLevel
    {
        private readonly ReadOnlyCollection<ChannelBuffer> _channels;

        protected PartLevel(
            int levelX,
            int levelY,
            Box2i region,
            IEnumerable<ChannelBuffer> channels)
        {
            if (levelX < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelX));
            }

            if (levelY < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelY));
            }

            List<ChannelBuffer> channelList = ModelValidation.CopySortedUnique(
                channels,
                static channel => channel.Name,
                nameof(channels),
                "channel buffer");
            if (channelList.Count == 0)
            {
                throw new ArgumentException("At least one channel buffer is required.", nameof(channels));
            }

            LevelX = levelX;
            LevelY = levelY;
            Region = region;
            _channels = channelList.AsReadOnly();
        }

        public int LevelX { get; }

        public int LevelY { get; }

        /// <summary>
        /// Absolute inclusive coordinates represented by this materialization.
        /// </summary>
        public Box2i Region { get; }

        public long Width => Region.Width;

        public long Height => Region.Height;

        public IReadOnlyList<ChannelBuffer> Channels => _channels;

        public ChannelBuffer GetChannel(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            foreach (ChannelBuffer channel in _channels)
            {
                if (StringComparer.Ordinal.Equals(channel.Name, name))
                {
                    return channel;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name), name, "The materialized region does not contain the requested channel.");
        }
    }

    /// <summary>
    /// Planar storage for a non-deep materialized region.
    /// </summary>
    public sealed class FlatLevel : PartLevel
    {
        public FlatLevel(
            int levelX,
            int levelY,
            Box2i region,
            IEnumerable<ChannelBuffer> channels)
            : base(levelX, levelY, region, channels)
        {
        }
    }

    /// <summary>
    /// Offset and count of one pixel's samples in a deep channel buffer.
    /// </summary>
    public readonly struct DeepSampleRange
    {
        internal DeepSampleRange(ulong offset, int count)
        {
            Offset = offset;
            Count = count;
        }

        public ulong Offset { get; }

        public int Count { get; }
    }

    /// <summary>
    /// Deep storage with contiguous per-pixel counts and native per-channel sample buffers.
    /// </summary>
    public sealed class DeepLevel : PartLevel
    {
        private readonly int[] _sampleCounts;
        private readonly ulong[] _sampleOffsets;
        private readonly ReadOnlyCollection<Channel> _channelDescriptions;
        private readonly Dictionary<string, DeepChannelLayout> _channelLayouts;

        public DeepLevel(
            int levelX,
            int levelY,
            Box2i region,
            ReadOnlySpan<int> sampleCounts,
            IEnumerable<ChannelBuffer> channels)
            : this(levelX, levelY, region, sampleCounts.ToArray(), channelDescriptions: null, channels)
        {
        }

        public DeepLevel(
            int levelX,
            int levelY,
            Box2i region,
            ReadOnlySpan<int> sampleCounts,
            IEnumerable<Channel> channelDescriptions,
            IEnumerable<ChannelBuffer> channels)
            : this(levelX, levelY, region, sampleCounts.ToArray(), channelDescriptions, channels)
        {
        }

        private DeepLevel(
            int levelX,
            int levelY,
            Box2i region,
            int[] sampleCounts,
            IEnumerable<Channel>? channelDescriptions,
            IEnumerable<ChannelBuffer> channels)
            : base(levelX, levelY, region, channels)
        {
            if (sampleCounts == null)
            {
                throw new ArgumentNullException(nameof(sampleCounts));
            }

            ulong pixelCount;
            try
            {
                pixelCount = ModelValidation.CountPixels(region);
            }
            catch (OverflowException exception)
            {
                throw new NotSupportedException("The region contains more pixels than a materialized deep count buffer can describe.", exception);
            }

            if (pixelCount > int.MaxValue)
            {
                throw new NotSupportedException(
                    $"The region contains {pixelCount} pixels; a materialized deep count buffer is limited to {int.MaxValue} entries.");
            }

            if ((ulong)sampleCounts.Length != pixelCount)
            {
                throw new ArgumentException(
                    $"The deep sample-count buffer has {sampleCounts.Length} entries; {pixelCount} entries are required.",
                    nameof(sampleCounts));
            }

            _sampleCounts = sampleCounts;
            _sampleOffsets = new ulong[_sampleCounts.Length];
            ulong total = 0;
            for (int i = 0; i < _sampleCounts.Length; i++)
            {
                int count = _sampleCounts[i];
                if (count < 0)
                {
                    throw new ArgumentException("Deep sample counts must be non-negative.", nameof(sampleCounts));
                }

                _sampleOffsets[i] = total;
                total = checked(total + (uint)count);
            }

            TotalSamples = total;

            List<Channel> descriptions;
            if (channelDescriptions == null)
            {
                descriptions = new List<Channel>(Channels.Count);
                foreach (ChannelBuffer channel in Channels)
                {
                    descriptions.Add(new Channel(channel.Name, channel.PixelType));
                }
            }
            else
            {
                descriptions = ModelValidation.CopySortedUnique(
                    channelDescriptions,
                    static channel => channel.Name,
                    nameof(channelDescriptions),
                    "deep channel description");
            }

            if (descriptions.Count != Channels.Count)
            {
                throw new ArgumentException(
                    "Every deep channel buffer requires one matching channel description.",
                    nameof(channelDescriptions));
            }

            _channelLayouts = new Dictionary<string, DeepChannelLayout>(descriptions.Count, StringComparer.Ordinal);
            for (int i = 0; i < descriptions.Count; i++)
            {
                Channel description = descriptions[i];
                ChannelBuffer channel = Channels[i];
                if (!StringComparer.Ordinal.Equals(description.Name, channel.Name) ||
                    description.PixelType != channel.PixelType)
                {
                    throw new ArgumentException(
                        $"Deep channel description '{description.Name}' does not match buffer '{channel.Name}'.",
                        nameof(channelDescriptions));
                }

                DeepChannelLayout layout = BuildChannelLayout(description);
                ulong expectedBytes = checked(
                    layout.TotalSamples * (uint)ModelValidation.PixelTypeSize(channel.PixelType));
                if (expectedBytes > ChannelBuffer.MaxMaterializedByteLength)
                {
                    throw new NotSupportedException(
                        $"Deep channel '{channel.Name}' requires {expectedBytes} bytes; a materialized channel buffer is limited to {ChannelBuffer.MaxMaterializedByteLength} bytes.");
                }

                if ((ulong)channel.ByteLength != expectedBytes)
                {
                    throw new ArgumentException(
                        $"Deep channel '{channel.Name}' has {channel.ByteLength} bytes; {expectedBytes} bytes are required.",
                        nameof(channels));
                }

                _channelLayouts.Add(description.Name, layout);
            }

            _channelDescriptions = descriptions.AsReadOnly();
        }

        /// <summary>
        /// Adopts parser-owned count storage without a second copy. The caller must transfer exclusive ownership.
        /// </summary>
        internal static DeepLevel Adopt(
            int levelX,
            int levelY,
            Box2i region,
            int[] sampleCounts,
            IEnumerable<ChannelBuffer> channels)
        {
            return new DeepLevel(levelX, levelY, region, sampleCounts, channelDescriptions: null, channels);
        }

        /// <summary>
        /// Adopts parser-owned count storage and applies immutable per-channel sampling descriptions.
        /// </summary>
        internal static DeepLevel Adopt(
            int levelX,
            int levelY,
            Box2i region,
            int[] sampleCounts,
            IEnumerable<Channel> channelDescriptions,
            IEnumerable<ChannelBuffer> channels)
        {
            return new DeepLevel(levelX, levelY, region, sampleCounts, channelDescriptions, channels);
        }

        public ReadOnlySpan<int> SampleCounts => _sampleCounts;

        public ulong TotalSamples { get; }

        internal IReadOnlyList<Channel> ChannelDescriptions => _channelDescriptions;

        public ulong GetChannelSampleCount(string channelName)
        {
            return GetChannelLayout(channelName).TotalSamples;
        }

        public DeepSampleRange GetSampleRange(int pixelIndex)
        {
            if ((uint)pixelIndex >= (uint)_sampleCounts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelIndex));
            }

            return new DeepSampleRange(_sampleOffsets[pixelIndex], _sampleCounts[pixelIndex]);
        }

        public DeepSampleRange GetSampleRange(string channelName, int pixelIndex)
        {
            if ((uint)pixelIndex >= (uint)_sampleCounts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelIndex));
            }

            DeepChannelLayout layout = GetChannelLayout(channelName);
            if (layout.SampledPixelIndices == null)
            {
                return new DeepSampleRange(_sampleOffsets[pixelIndex], _sampleCounts[pixelIndex]);
            }

            int sampledIndex = Array.BinarySearch(layout.SampledPixelIndices, pixelIndex);
            if (sampledIndex >= 0)
            {
                return new DeepSampleRange(layout.SampleOffsets[sampledIndex], _sampleCounts[pixelIndex]);
            }

            int insertionIndex = ~sampledIndex;
            ulong offset = insertionIndex < layout.SampleOffsets.Length
                ? layout.SampleOffsets[insertionIndex]
                : layout.TotalSamples;
            return new DeepSampleRange(offset, 0);
        }

        public ReadOnlySpan<byte> GetSamples(string channelName, int pixelIndex)
        {
            ChannelBuffer channel = GetChannel(channelName);
            DeepSampleRange range = GetSampleRange(channelName, pixelIndex);
            int elementSize = ModelValidation.PixelTypeSize(channel.PixelType);
            ulong byteOffset = checked(range.Offset * (uint)elementSize);
            int byteLength = checked(range.Count * elementSize);
            return channel.Data.Slice(checked((int)byteOffset), byteLength);
        }

        private DeepChannelLayout BuildChannelLayout(Channel channel)
        {
            if (channel.XSampling == 1 && channel.YSampling == 1)
            {
                return new DeepChannelLayout(sampledPixelIndices: null, _sampleOffsets, TotalSamples);
            }

            ulong sampledPixelCount = ModelValidation.CountSamples(
                Region,
                channel.XSampling,
                channel.YSampling);
            int[] sampledPixelIndices = new int[checked((int)sampledPixelCount)];
            ulong[] sampleOffsets = new ulong[sampledPixelIndices.Length];
            int width = checked((int)Region.Width);
            int sampledIndex = 0;
            ulong total = 0;
            for (int pixelIndex = 0; pixelIndex < _sampleCounts.Length; pixelIndex++)
            {
                long x = (long)Region.MinX + (pixelIndex % width);
                long y = (long)Region.MinY + (pixelIndex / width);
                if (x % channel.XSampling != 0 || y % channel.YSampling != 0)
                {
                    continue;
                }

                sampledPixelIndices[sampledIndex] = pixelIndex;
                sampleOffsets[sampledIndex] = total;
                total = checked(total + (uint)_sampleCounts[pixelIndex]);
                sampledIndex++;
            }

            if (sampledIndex != sampledPixelIndices.Length)
            {
                throw new InvalidOperationException("The sampled deep pixel count does not match the channel geometry.");
            }

            return new DeepChannelLayout(sampledPixelIndices, sampleOffsets, total);
        }

        private DeepChannelLayout GetChannelLayout(string channelName)
        {
            if (channelName == null)
            {
                throw new ArgumentNullException(nameof(channelName));
            }

            if (_channelLayouts.TryGetValue(channelName, out DeepChannelLayout? layout))
            {
                return layout;
            }

            throw new ArgumentOutOfRangeException(
                nameof(channelName),
                channelName,
                "The materialized region does not contain the requested channel.");
        }

        private sealed class DeepChannelLayout
        {
            public DeepChannelLayout(int[]? sampledPixelIndices, ulong[] sampleOffsets, ulong totalSamples)
            {
                SampledPixelIndices = sampledPixelIndices;
                SampleOffsets = sampleOffsets;
                TotalSamples = totalSamples;
            }

            public int[]? SampledPixelIndices { get; }

            public ulong[] SampleOffsets { get; }

            public ulong TotalSamples { get; }
        }
    }

    /// <summary>
    /// One scanline, tiled, deep-scanline, or deep-tiled image part.
    /// </summary>
    public sealed class Part
    {
        private readonly ReadOnlyCollection<PartLevel> _levels;

        /// <param name="isComplete">
        /// True only when every logical level is represented by one full-region materialization.
        /// Block, tile, scanline-range, and base-only results must pass false.
        /// </param>
        public Part(Header header, IEnumerable<PartLevel> levels, bool isComplete = false)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            if (levels == null)
            {
                throw new ArgumentNullException(nameof(levels));
            }

            List<PartLevel> levelList = new List<PartLevel>();
            foreach (PartLevel level in levels)
            {
                if (level == null)
                {
                    throw new ArgumentException("The level collection must not contain null elements.", nameof(levels));
                }

                levelList.Add(level);
            }

            if (levelList.Count == 0)
            {
                throw new ArgumentException("At least one materialized region is required.", nameof(levels));
            }

            levelList.Sort(CompareLevels);
            for (int i = 1; i < levelList.Count; i++)
            {
                PartLevel previous = levelList[i - 1];
                PartLevel current = levelList[i];
                if (previous.LevelX == current.LevelX && previous.LevelY == current.LevelY &&
                    RegionsEqual(previous.Region, current.Region))
                {
                    throw new ArgumentException(
                        $"Duplicate materialized region for level ({current.LevelX}, {current.LevelY}).",
                        nameof(levels));
                }
            }

            ValidateLevels(header, levelList, isComplete, nameof(levels));
            IsComplete = isComplete;
            _levels = levelList.AsReadOnly();
        }

        public Header Header { get; }

        public long Width => Header.DataWindow.Width;

        public long Height => Header.DataWindow.Height;

        /// <summary>
        /// Whether all levels are fully materialized, rather than a partial block/range result.
        /// </summary>
        public bool IsComplete { get; }

        public IReadOnlyList<PartLevel> Levels => _levels;

        public PartLevel GetLevel(int levelX, int levelY)
        {
            PartLevel? result = null;
            foreach (PartLevel level in _levels)
            {
                if (level.LevelX != levelX || level.LevelY != levelY)
                {
                    continue;
                }

                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"Level ({levelX}, {levelY}) has multiple materialized regions; use GetLevels instead.");
                }

                result = level;
            }

            return result ?? throw new ArgumentOutOfRangeException(
                nameof(levelX),
                $"The part does not contain level ({levelX}, {levelY}).");
        }

        public IReadOnlyList<PartLevel> GetLevels(int levelX, int levelY)
        {
            if (levelX < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelX));
            }

            if (levelY < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelY));
            }

            List<PartLevel> result = new List<PartLevel>();
            foreach (PartLevel level in _levels)
            {
                if (level.LevelX == levelX && level.LevelY == levelY)
                {
                    result.Add(level);
                }
            }

            return result.AsReadOnly();
        }

        private static int CompareLevels(PartLevel left, PartLevel right)
        {
            int comparison = left.LevelY.CompareTo(right.LevelY);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.LevelX.CompareTo(right.LevelX);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Region.MinY.CompareTo(right.Region.MinY);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Region.MinX.CompareTo(right.Region.MinX);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Region.MaxY.CompareTo(right.Region.MaxY);
            return comparison != 0 ? comparison : left.Region.MaxX.CompareTo(right.Region.MaxX);
        }

        private static void ValidateLevels(
            Header header,
            List<PartLevel> levels,
            bool isComplete,
            string parameterName)
        {
            foreach (PartLevel level in levels)
            {
                if ((level is DeepLevel) != header.IsDeep)
                {
                    throw new ArgumentException(
                        header.IsDeep ? "Deep parts require deep materializations." : "Flat parts require flat materializations.",
                        parameterName);
                }

                LevelGeometry expectedGeometry = GetExpectedLevel(header, level.LevelX, level.LevelY, parameterName);
                if (!expectedGeometry.Region.Contains(level.Region))
                {
                    throw new ArgumentException(
                        $"Region [{level.Region.MinX}, {level.Region.MinY}] - [{level.Region.MaxX}, {level.Region.MaxY}] lies outside level ({level.LevelX}, {level.LevelY}).",
                        parameterName);
                }

                ValidateChannels(header, level, parameterName);
            }

            if (isComplete)
            {
                ValidateCompleteLevels(header, levels, parameterName);
            }
        }

        private static void ValidateChannels(Header header, PartLevel level, string parameterName)
        {
            if (level.Channels.Count != header.Channels.Count)
            {
                throw new ArgumentException("Every materialized region must contain every header channel.", parameterName);
            }

            for (int i = 0; i < header.Channels.Count; i++)
            {
                Channel expected = header.Channels[i];
                ChannelBuffer actual = level.Channels[i];
                if (!StringComparer.Ordinal.Equals(expected.Name, actual.Name) || expected.PixelType != actual.PixelType)
                {
                    throw new ArgumentException(
                        $"Materialized channel '{actual.Name}' does not match header channel '{expected.Name}'.",
                        parameterName);
                }

                if (level is DeepLevel deepLevel)
                {
                    Channel actualDescription = deepLevel.ChannelDescriptions[i];
                    if (!StringComparer.Ordinal.Equals(expected.Name, actualDescription.Name) ||
                        expected.PixelType != actualDescription.PixelType ||
                        expected.XSampling != actualDescription.XSampling ||
                        expected.YSampling != actualDescription.YSampling)
                    {
                        throw new ArgumentException(
                            $"Deep channel '{actual.Name}' sampling does not match its header description.",
                            parameterName);
                    }
                }
                else
                {
                    ulong expectedBytes;
                    try
                    {
                        ulong sampleCount = ModelValidation.CountSamples(
                            level.Region,
                            expected.XSampling,
                            expected.YSampling);
                        expectedBytes = checked(sampleCount * (uint)ModelValidation.PixelTypeSize(expected.PixelType));
                    }
                    catch (OverflowException exception)
                    {
                        throw new NotSupportedException(
                            $"Channel '{expected.Name}' exceeds the materialized buffer address space.",
                            exception);
                    }

                    if (expectedBytes > ChannelBuffer.MaxMaterializedByteLength)
                    {
                        throw new NotSupportedException(
                            $"Channel '{expected.Name}' requires {expectedBytes} bytes; a materialized channel buffer is limited to {ChannelBuffer.MaxMaterializedByteLength} bytes.");
                    }

                    if ((ulong)actual.ByteLength != expectedBytes)
                    {
                        throw new ArgumentException(
                            $"Channel '{actual.Name}' has {actual.ByteLength} bytes; {expectedBytes} bytes are required for its sampled absolute region.",
                            parameterName);
                    }
                }
            }
        }

        private static void ValidateCompleteLevels(Header header, List<PartLevel> levels, string parameterName)
        {
            List<LevelGeometry> expectedLevels = BuildExpectedLevels(header);
            if (levels.Count != expectedLevels.Count)
            {
                throw new ArgumentException(
                    "A complete part requires exactly one full-region materialization for every logical level.",
                    parameterName);
            }

            for (int i = 0; i < expectedLevels.Count; i++)
            {
                PartLevel actual = levels[i];
                LevelGeometry expected = expectedLevels[i];
                if (actual.LevelX != expected.LevelX || actual.LevelY != expected.LevelY ||
                    !RegionsEqual(actual.Region, expected.Region))
                {
                    throw new ArgumentException(
                        "A complete part requires exactly one full-region materialization for every logical level.",
                        parameterName);
                }
            }
        }

        private static LevelGeometry GetExpectedLevel(
            Header header,
            int levelX,
            int levelY,
            string parameterName)
        {
            if (levelX < 0 || levelY < 0)
            {
                throw new ArgumentException("Level indices must be non-negative.", parameterName);
            }

            if (!header.IsTiled || header.Tiles!.LevelMode == TileLevelMode.OneLevel)
            {
                if (levelX != 0 || levelY != 0)
                {
                    throw new ArgumentException("This part has only level (0, 0).", parameterName);
                }

                return new LevelGeometry(0, 0, header.DataWindow);
            }

            int xLevelCount = GetLevelCount(header.DataWindow.Width, header.Tiles.RoundingMode);
            int yLevelCount = GetLevelCount(header.DataWindow.Height, header.Tiles.RoundingMode);
            if (header.Tiles.LevelMode == TileLevelMode.MipmapLevels)
            {
                int levelCount = Math.Max(xLevelCount, yLevelCount);
                if (levelX != levelY || levelX >= levelCount)
                {
                    throw new ArgumentException("The mipmap level indices are outside the header tile mode.", parameterName);
                }
            }
            else if (levelX >= xLevelCount || levelY >= yLevelCount)
            {
                throw new ArgumentException("The ripmap level indices are outside the header tile mode.", parameterName);
            }

            long width = GetLevelSize(header.DataWindow.Width, levelX, header.Tiles.RoundingMode);
            long height = GetLevelSize(header.DataWindow.Height, levelY, header.Tiles.RoundingMode);
            return new LevelGeometry(levelX, levelY, CreateLevelRegion(header.DataWindow, width, height));
        }

        private static List<LevelGeometry> BuildExpectedLevels(Header header)
        {
            List<LevelGeometry> result = new List<LevelGeometry>();
            if (!header.IsTiled || header.Tiles!.LevelMode == TileLevelMode.OneLevel)
            {
                result.Add(new LevelGeometry(0, 0, header.DataWindow));
                return result;
            }

            TileRoundingMode rounding = header.Tiles.RoundingMode;
            int xLevels = GetLevelCount(header.DataWindow.Width, rounding);
            int yLevels = GetLevelCount(header.DataWindow.Height, rounding);
            if (header.Tiles.LevelMode == TileLevelMode.MipmapLevels)
            {
                int count = Math.Max(xLevels, yLevels);
                for (int level = 0; level < count; level++)
                {
                    result.Add(new LevelGeometry(
                        level,
                        level,
                        CreateLevelRegion(
                            header.DataWindow,
                            GetLevelSize(header.DataWindow.Width, level, rounding),
                            GetLevelSize(header.DataWindow.Height, level, rounding))));
                }
            }
            else
            {
                for (int levelY = 0; levelY < yLevels; levelY++)
                {
                    for (int levelX = 0; levelX < xLevels; levelX++)
                    {
                        result.Add(new LevelGeometry(
                            levelX,
                            levelY,
                            CreateLevelRegion(
                                header.DataWindow,
                                GetLevelSize(header.DataWindow.Width, levelX, rounding),
                                GetLevelSize(header.DataWindow.Height, levelY, rounding))));
                    }
                }
            }

            return result;
        }

        private static int GetLevelCount(long size, TileRoundingMode rounding)
        {
            int count = 1;
            while (size > 1)
            {
                size = rounding == TileRoundingMode.RoundUp ? (size / 2L) + (size & 1L) : size / 2L;
                count++;
            }

            return count;
        }

        private static long GetLevelSize(long size, int level, TileRoundingMode rounding)
        {
            for (int i = 0; i < level && size > 1; i++)
            {
                size = rounding == TileRoundingMode.RoundUp ? (size / 2L) + (size & 1L) : size / 2L;
            }

            return Math.Max(size, 1L);
        }

        private static Box2i CreateLevelRegion(Box2i dataWindow, long width, long height)
        {
            int maxX = checked((int)((long)dataWindow.MinX + width - 1L));
            int maxY = checked((int)((long)dataWindow.MinY + height - 1L));
            return new Box2i(dataWindow.MinX, dataWindow.MinY, maxX, maxY);
        }

        private static bool RegionsEqual(Box2i left, Box2i right)
        {
            return left.MinX == right.MinX && left.MinY == right.MinY &&
                left.MaxX == right.MaxX && left.MaxY == right.MaxY;
        }

        private readonly struct LevelGeometry
        {
            public LevelGeometry(int levelX, int levelY, Box2i region)
            {
                LevelX = levelX;
                LevelY = levelY;
                Region = region;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public Box2i Region { get; }
        }
    }

    /// <summary>
    /// Unified v3 image containing one or more ordered parts.
    /// </summary>
    public sealed class Image
    {
        private readonly ReadOnlyCollection<Part> _parts;

        public Image(IEnumerable<Part> parts)
        {
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            List<Part> partList = new List<Part>();
            foreach (Part part in parts)
            {
                if (part == null)
                {
                    throw new ArgumentException("The part collection must not contain null elements.", nameof(parts));
                }

                partList.Add(part);
            }

            if (partList.Count == 0)
            {
                throw new ArgumentException("At least one image part is required.", nameof(parts));
            }

            if (partList.Count > 1)
            {
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Part part in partList)
                {
                    if (part.Header.Name.Length == 0)
                    {
                        throw new ArgumentException("Every multipart part requires a non-empty name.", nameof(parts));
                    }

                    if (!names.Add(part.Header.Name))
                    {
                        throw new ArgumentException($"Duplicate multipart part name '{part.Header.Name}'.", nameof(parts));
                    }
                }
            }

            _parts = partList.AsReadOnly();
        }

        public IReadOnlyList<Part> Parts => _parts;

        public bool IsMultipart => _parts.Count > 1;

        public Part GetPart(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            foreach (Part part in _parts)
            {
                if (StringComparer.Ordinal.Equals(part.Header.Name, name))
                {
                    return part;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name), name, "The image does not contain the requested part.");
        }
    }
}
