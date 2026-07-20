using System;
using TinyEXR.V3.IO;

namespace TinyEXR.V3
{
    /// <summary>
    /// Observable lifecycle of an EXR reader.
    /// </summary>
    public enum ReaderState
    {
        Created = 0,
        ReadingPrefix = 1,
        ReadingHeaders = 2,
        ReadingOffsetTables = 3,
        Ready = 4,
        Faulted = 5,
        Disposed = 6,
    }

    /// <summary>
    /// Resource ceilings applied before allocating attacker-controlled EXR structures.
    /// </summary>
    public sealed class ReaderLimits
    {
        public ReaderLimits(
            long maximumHeaderByteCount = 64L * 1024L * 1024L,
            int maximumParts = 1024,
            int maximumAttributesPerPart = 4096,
            int maximumTotalAttributes = 16 * 1024,
            int maximumChannelsPerPart = 4096,
            int maximumAttributeByteCount = 16 * 1024 * 1024,
            long maximumTotalAttributeByteCount = 64L * 1024L * 1024L,
            int maximumBlocksPerPart = 8 * 1024 * 1024,
            long maximumTotalBlocks = 8L * 1024L * 1024L,
            long maximumOffsetTableByteCount = 64L * 1024L * 1024L,
            int maximumReadRequestByteCount = 64 * 1024,
            long maximumDimension = 1L << 20,
            int maximumCompressedBlockByteCount = 256 * 1024 * 1024,
            int maximumUncompressedBlockByteCount = 256 * 1024 * 1024,
            long maximumMaterializedByteCount = int.MaxValue,
            long maximumDeepSampleCount = 256L * 1024L * 1024L)
        {
            MaximumHeaderByteCount = Positive(maximumHeaderByteCount, nameof(maximumHeaderByteCount));
            MaximumParts = Positive(maximumParts, nameof(maximumParts));
            MaximumAttributesPerPart = Positive(maximumAttributesPerPart, nameof(maximumAttributesPerPart));
            MaximumTotalAttributes = Positive(maximumTotalAttributes, nameof(maximumTotalAttributes));
            MaximumChannelsPerPart = Positive(maximumChannelsPerPart, nameof(maximumChannelsPerPart));
            MaximumAttributeByteCount = Positive(maximumAttributeByteCount, nameof(maximumAttributeByteCount));
            MaximumTotalAttributeByteCount = Positive(
                maximumTotalAttributeByteCount,
                nameof(maximumTotalAttributeByteCount));
            MaximumBlocksPerPart = Positive(maximumBlocksPerPart, nameof(maximumBlocksPerPart));
            MaximumTotalBlocks = Positive(maximumTotalBlocks, nameof(maximumTotalBlocks));
            MaximumOffsetTableByteCount = Positive(
                maximumOffsetTableByteCount,
                nameof(maximumOffsetTableByteCount));
            MaximumReadRequestByteCount = Positive(
                maximumReadRequestByteCount,
                nameof(maximumReadRequestByteCount));
            MaximumDimension = Positive(maximumDimension, nameof(maximumDimension));
            MaximumCompressedBlockByteCount = Positive(
                maximumCompressedBlockByteCount,
                nameof(maximumCompressedBlockByteCount));
            MaximumUncompressedBlockByteCount = Positive(
                maximumUncompressedBlockByteCount,
                nameof(maximumUncompressedBlockByteCount));
            MaximumMaterializedByteCount = Positive(
                maximumMaterializedByteCount,
                nameof(maximumMaterializedByteCount));
            MaximumDeepSampleCount = Positive(
                maximumDeepSampleCount,
                nameof(maximumDeepSampleCount));

            if (MaximumReadRequestByteCount < sizeof(long))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumReadRequestByteCount),
                    maximumReadRequestByteCount,
                    "The read request limit must allow one 64-bit offset entry.");
            }
        }

        public long MaximumHeaderByteCount { get; }

        public int MaximumParts { get; }

        public int MaximumAttributesPerPart { get; }

        public int MaximumTotalAttributes { get; }

        public int MaximumChannelsPerPart { get; }

        public int MaximumAttributeByteCount { get; }

        public long MaximumTotalAttributeByteCount { get; }

        public int MaximumBlocksPerPart { get; }

        public long MaximumTotalBlocks { get; }

        public long MaximumOffsetTableByteCount { get; }

        public int MaximumReadRequestByteCount { get; }

        public long MaximumDimension { get; }

        public int MaximumCompressedBlockByteCount { get; }

        public int MaximumUncompressedBlockByteCount { get; }

        public long MaximumMaterializedByteCount { get; }

        public long MaximumDeepSampleCount { get; }

        private static int Positive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "The limit must be positive.");
            }

            return value;
        }

        private static long Positive(long value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "The limit must be positive.");
            }

            return value;
        }
    }

    /// <summary>
    /// Reader ownership and resource policy.
    /// </summary>
    public sealed class ReaderOptions
    {
        public ReaderOptions(ReaderLimits? limits = null, bool leaveOpen = true)
        {
            Limits = limits ?? new ReaderLimits();
            LeaveOpen = leaveOpen;
        }

        public ReaderLimits Limits { get; }

        /// <summary>
        /// Whether disposing the reader leaves the externally supplied source open.
        /// </summary>
        public bool LeaveOpen { get; }
    }

    /// <summary>
    /// Diagnostic attached to an Unsupported result caused by a configured resource ceiling.
    /// </summary>
    public sealed class ReaderLimitExceededException : Exception
    {
        internal ReaderLimitExceededException(string limitName, long actual, long maximum)
            : base($"Reader limit '{limitName}' was exceeded: {actual} > {maximum}.")
        {
            LimitName = limitName;
            Actual = actual;
            Maximum = maximum;
        }

        public string LimitName { get; }

        public long Actual { get; }

        public long Maximum { get; }
    }

    /// <summary>
    /// Status and retry information for one reader operation.
    /// </summary>
    public readonly struct ReaderResult
    {
        internal ReaderResult(
            ExrResult status,
            DataRange? pending,
            Exception? error,
            long bytesWritten = 0)
        {
            Status = status;
            Pending = pending;
            Error = error;
            BytesWritten = bytesWritten;
        }

        public ExrResult Status { get; }

        public DataRange? Pending { get; }

        public Exception? Error { get; }

        public long BytesWritten { get; }

        public bool IsSuccess => Status == ExrResult.Success;
    }

    /// <summary>
    /// Status and atomically published value for a materializing reader operation.
    /// </summary>
    public readonly struct ReaderResult<T>
        where T : class
    {
        internal ReaderResult(ReaderResult operation, T? value)
        {
            Operation = operation;
            Value = value;
        }

        public ReaderResult Operation { get; }

        public ExrResult Status => Operation.Status;

        public DataRange? Pending => Operation.Pending;

        public Exception? Error => Operation.Error;

        public long BytesWritten => Operation.BytesWritten;

        public bool IsSuccess => Operation.IsSuccess;

        /// <summary>Non-null only when the operation completed successfully.</summary>
        public T? Value { get; }
    }

    /// <summary>
    /// Payload-independent location and geometry of one logical EXR block.
    /// </summary>
    public readonly struct BlockInfo
    {
        internal BlockInfo(
            int partIndex,
            int blockIndex,
            bool isTiled,
            bool isDeep,
            int levelX,
            int levelY,
            int tileX,
            int tileY,
            Box2i region,
            int chunkHeaderByteCount,
            ulong? uncompressedByteCount,
            long fileOffset)
        {
            PartIndex = partIndex;
            BlockIndex = blockIndex;
            IsTiled = isTiled;
            IsDeep = isDeep;
            LevelX = levelX;
            LevelY = levelY;
            TileX = tileX;
            TileY = tileY;
            Region = region;
            ChunkHeaderByteCount = chunkHeaderByteCount;
            UncompressedByteCount = uncompressedByteCount;
            FileOffset = fileOffset;
        }

        public int PartIndex { get; }

        public int BlockIndex { get; }

        public bool IsTiled { get; }

        public bool IsDeep { get; }

        public int LevelX { get; }

        public int LevelY { get; }

        /// <summary>Tile X coordinate, or -1 for a scanline block.</summary>
        public int TileX { get; }

        /// <summary>Tile Y coordinate, or -1 for a scanline block.</summary>
        public int TileY { get; }

        public Box2i Region { get; }

        public int ChunkHeaderByteCount { get; }

        /// <summary>Canonical flat byte count; null for deep blocks.</summary>
        public ulong? UncompressedByteCount { get; }

        /// <summary>Absolute chunk offset, or zero when the offset table marks the block missing.</summary>
        public long FileOffset { get; }

        public bool IsMissing => FileOffset == 0;
    }

    /// <summary>Named writable storage for one deep block channel.</summary>
    public sealed class DeepChannelDestination
    {
        public DeepChannelDestination(string name, Memory<byte> data)
        {
            ModelValidation.ValidateName(name, nameof(name), allowEmpty: false);
            Name = name;
            Data = data;
        }

        public string Name { get; }

        public Memory<byte> Data { get; }
    }
}
