using System;
using TinyEXR.V3.IO;

namespace TinyEXR.V3
{
    /// <summary>
    /// Observable lifecycle of a streaming EXR writer.
    /// </summary>
    public enum WriterState
    {
        Created = 0,
        Beginning = 1,
        Streaming = 2,
        WritingBlock = 3,
        Ending = 4,
        Complete = 5,
        Faulted = 6,
        Disposed = 7,
    }

    /// <summary>
    /// Resource ceilings applied before allocating writer metadata and block buffers.
    /// </summary>
    public sealed class WriterLimits
    {
        public WriterLimits(
            int maximumParts = 1024,
            int maximumChannelsPerPart = 4096,
            int maximumAttributesPerPart = 4096,
            long maximumHeaderByteCount = 64L * 1024L * 1024L,
            int maximumBlocksPerPart = 8 * 1024 * 1024,
            long maximumTotalBlocks = 8L * 1024L * 1024L,
            long maximumOffsetTableByteCount = 64L * 1024L * 1024L,
            long maximumDimension = 1L << 20,
            int maximumUncompressedBlockByteCount = 256 * 1024 * 1024,
            int maximumEncodedBlockByteCount = 256 * 1024 * 1024,
            long maximumDeepSampleCount = 256L * 1024L * 1024L)
        {
            MaximumParts = Positive(maximumParts, nameof(maximumParts));
            MaximumChannelsPerPart = Positive(maximumChannelsPerPart, nameof(maximumChannelsPerPart));
            MaximumAttributesPerPart = Positive(maximumAttributesPerPart, nameof(maximumAttributesPerPart));
            MaximumHeaderByteCount = Positive(maximumHeaderByteCount, nameof(maximumHeaderByteCount));
            MaximumBlocksPerPart = Positive(maximumBlocksPerPart, nameof(maximumBlocksPerPart));
            MaximumTotalBlocks = Positive(maximumTotalBlocks, nameof(maximumTotalBlocks));
            MaximumOffsetTableByteCount = Positive(
                maximumOffsetTableByteCount,
                nameof(maximumOffsetTableByteCount));
            MaximumDimension = Positive(maximumDimension, nameof(maximumDimension));
            MaximumUncompressedBlockByteCount = Positive(
                maximumUncompressedBlockByteCount,
                nameof(maximumUncompressedBlockByteCount));
            MaximumEncodedBlockByteCount = Positive(
                maximumEncodedBlockByteCount,
                nameof(maximumEncodedBlockByteCount));
            MaximumDeepSampleCount = Positive(
                maximumDeepSampleCount,
                nameof(maximumDeepSampleCount));
        }

        public int MaximumParts { get; }

        public int MaximumChannelsPerPart { get; }

        public int MaximumAttributesPerPart { get; }

        public long MaximumHeaderByteCount { get; }

        public int MaximumBlocksPerPart { get; }

        public long MaximumTotalBlocks { get; }

        public long MaximumOffsetTableByteCount { get; }

        public long MaximumDimension { get; }

        public int MaximumUncompressedBlockByteCount { get; }

        public int MaximumEncodedBlockByteCount { get; }

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
    /// Writer ownership and resource policy.
    /// </summary>
    public sealed class WriterOptions
    {
        public WriterOptions(
            WriterLimits? limits = null,
            bool leaveOpen = true,
            bool forceMultipart = false)
        {
            Limits = limits ?? new WriterLimits();
            LeaveOpen = leaveOpen;
            ForceMultipart = forceMultipart;
        }

        public WriterLimits Limits { get; }

        /// <summary>
        /// Whether disposing the writer leaves the externally supplied sink open.
        /// </summary>
        public bool LeaveOpen { get; }

        /// <summary>
        /// Whether a single-part image uses multipart headers and chunk framing.
        /// Images with multiple parts are always multipart.
        /// </summary>
        public bool ForceMultipart { get; }
    }

    /// <summary>
    /// Diagnostic attached to an Unsupported result caused by a configured resource ceiling.
    /// </summary>
    public sealed class WriterLimitExceededException : Exception
    {
        internal WriterLimitExceededException(string limitName, long actual, long maximum)
            : base($"Writer limit '{limitName}' was exceeded: {actual} > {maximum}.")
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
    /// Status and confirmed output progress for one writer operation.
    /// </summary>
    public readonly struct WriterResult
    {
        internal WriterResult(
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

        /// <summary>
        /// Optional retry information propagated by a non-blocking sink.
        /// </summary>
        public DataRange? Pending { get; }

        public Exception? Error { get; }

        /// <summary>
        /// Bytes whose transfer was confirmed during this invocation.
        /// </summary>
        public long BytesWritten { get; }

        public bool IsSuccess => Status == ExrResult.Success;
    }

    /// <summary>
    /// Status and atomically published value for a materializing writer operation.
    /// </summary>
    public readonly struct WriterResult<T>
        where T : class
    {
        internal WriterResult(WriterResult operation, T? value)
        {
            Operation = operation;
            Value = value;
        }

        public WriterResult Operation { get; }

        public ExrResult Status => Operation.Status;

        public DataRange? Pending => Operation.Pending;

        public Exception? Error => Operation.Error;

        public long BytesWritten => Operation.BytesWritten;

        public bool IsSuccess => Operation.IsSuccess;

        /// <summary>Non-null only when the operation completed successfully.</summary>
        public T? Value { get; }
    }
}
