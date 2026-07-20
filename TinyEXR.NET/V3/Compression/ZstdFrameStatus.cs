namespace TinyEXR.V3.Codecs
{
    /// <summary>
    /// Result of parsing or processing one Zstandard frame.
    /// </summary>
    internal enum ZstdFrameStatus
    {
        Success = 0,
        Skipped = 1,
        UnsupportedCompressedBlock = 2,
        DictionaryNotSupported = 3,

        InvalidMagic = -1,
        Corrupt = -2,
        Truncated = -3,
        DestinationTooSmall = -4,
        WindowTooLarge = -5,
        ContentSizeTooLarge = -6,
        ContentSizeMismatch = -7,
        ChecksumMismatch = -8,
    }

    internal static class ZstdFrameLimits
    {
        // EXR compression works on bounded chunks. Keeping a hard ceiling here prevents
        // hostile standalone frames from requesting multi-gigabyte history buffers.
        internal const int MaximumOutputSize = 256 * 1024 * 1024;
        internal const ulong MaximumWindowSize = 256UL * 1024UL * 1024UL;
        internal const int MaximumBlockSize = 128 * 1024;
    }
}
