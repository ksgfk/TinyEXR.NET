using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyEXR.V3.Format
{
    /// <summary>
    /// OpenEXR version-field state needed by the format and payload layers.
    /// </summary>
    internal readonly struct ParsedFileFlags
    {
        public ParsedFileFlags(bool tiled, bool longNames, bool nonImage, bool multipart)
        {
            Tiled = tiled;
            LongNames = longNames;
            NonImage = nonImage;
            Multipart = multipart;
        }

        public bool Tiled { get; }

        public bool LongNames { get; }

        public bool NonImage { get; }

        public bool Multipart { get; }
    }

    /// <summary>
    /// One raw header attribute together with its exact source boundaries.
    /// </summary>
    internal sealed class ParsedAttribute
    {
        public ParsedAttribute(
            HeaderAttribute value,
            int attributeStart,
            int dataStart,
            int attributeEnd)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            AttributeStart = attributeStart;
            DataStart = dataStart;
            AttributeEnd = attributeEnd;
        }

        public HeaderAttribute Value { get; }

        public int AttributeStart { get; }

        public int DataStart { get; }

        public int AttributeEnd { get; }
    }

    /// <summary>
    /// Geometry which can be determined without touching a compressed chunk.
    /// </summary>
    internal readonly struct ParsedBlockGeometry
    {
        public ParsedBlockGeometry(
            bool isTiled,
            bool isDeep,
            int levelX,
            int levelY,
            int tileX,
            int tileY,
            Box2i region,
            int chunkHeaderByteCount,
            ulong? uncompressedByteCount)
        {
            IsTiled = isTiled;
            IsDeep = isDeep;
            LevelX = levelX;
            LevelY = levelY;
            TileX = tileX;
            TileY = tileY;
            Region = region;
            ChunkHeaderByteCount = chunkHeaderByteCount;
            UncompressedByteCount = uncompressedByteCount;
        }

        public bool IsTiled { get; }

        public bool IsDeep { get; }

        public int LevelX { get; }

        public int LevelY { get; }

        /// <summary>
        /// Tile coordinate, or -1 for a scanline block.
        /// </summary>
        public int TileX { get; }

        /// <summary>
        /// Tile coordinate, or -1 for a scanline block.
        /// </summary>
        public int TileY { get; }

        public Box2i Region { get; }

        public int ChunkHeaderByteCount { get; }

        /// <summary>
        /// Canonical flat bytes before compression. Deep block sample bytes are data-dependent.
        /// </summary>
        public ulong? UncompressedByteCount { get; }
    }

    /// <summary>
    /// One offset-table entry and its payload-independent block geometry.
    /// </summary>
    internal readonly struct ParsedChunkIndex
    {
        public ParsedChunkIndex(int index, ulong fileOffset, ParsedBlockGeometry geometry)
        {
            Index = index;
            FileOffset = fileOffset;
            Geometry = geometry;
        }

        public int Index { get; }

        public ulong FileOffset { get; }

        public bool IsMissing => FileOffset == 0;

        public ParsedBlockGeometry Geometry { get; }
    }

    /// <summary>
    /// Parsed metadata and random-access index for one EXR part.
    /// </summary>
    internal sealed class ParsedPartIndex
    {
        private readonly ReadOnlyCollection<ParsedAttribute> _rawAttributes;
        private readonly ReadOnlyCollection<ParsedChunkIndex> _chunks;

        public ParsedPartIndex(
            int partIndex,
            Header header,
            IEnumerable<ParsedAttribute> rawAttributes,
            uint? declaredChunkCount,
            int headerStart,
            int headerEnd,
            int offsetTableStart,
            int offsetTableEnd,
            ParsedChunkIndex[] chunks)
        {
            PartIndex = partIndex;
            Header = header ?? throw new ArgumentNullException(nameof(header));
            _rawAttributes = new List<ParsedAttribute>(rawAttributes).AsReadOnly();
            DeclaredChunkCount = declaredChunkCount;
            HeaderStart = headerStart;
            HeaderEnd = headerEnd;
            OffsetTableStart = offsetTableStart;
            OffsetTableEnd = offsetTableEnd;
            _chunks = Array.AsReadOnly(chunks ?? throw new ArgumentNullException(nameof(chunks)));

            int missing = 0;
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].IsMissing)
                {
                    missing++;
                }
            }

            MissingChunkCount = missing;
        }

        public int PartIndex { get; }

        public Header Header { get; }

        public IReadOnlyList<ParsedAttribute> RawAttributes => _rawAttributes;

        public uint? DeclaredChunkCount { get; }

        public int HeaderStart { get; }

        public int HeaderEnd { get; }

        public int OffsetTableStart { get; }

        public int OffsetTableEnd { get; }

        public IReadOnlyList<ParsedChunkIndex> Chunks => _chunks;

        public int MissingChunkCount { get; }
    }

    /// <summary>
    /// Header-only parse result for an in-memory EXR file.
    /// </summary>
    internal sealed class ParsedFile
    {
        private readonly ReadOnlyCollection<ParsedPartIndex> _parts;

        public ParsedFile(
            uint rawVersionField,
            int fileVersion,
            ParsedFileFlags flags,
            int headersStart,
            int headersEnd,
            int offsetTablesStart,
            int offsetTablesEnd,
            int fileLength,
            IEnumerable<ParsedPartIndex> parts)
        {
            RawVersionField = rawVersionField;
            FileVersion = fileVersion;
            Flags = flags;
            HeadersStart = headersStart;
            HeadersEnd = headersEnd;
            OffsetTablesStart = offsetTablesStart;
            OffsetTablesEnd = offsetTablesEnd;
            FileLength = fileLength;
            _parts = new List<ParsedPartIndex>(parts).AsReadOnly();
        }

        public uint RawVersionField { get; }

        public int FileVersion { get; }

        public ParsedFileFlags Flags { get; }

        public int HeadersStart { get; }

        public int HeadersEnd { get; }

        public int OffsetTablesStart { get; }

        public int OffsetTablesEnd { get; }

        public int PixelDataStart => OffsetTablesEnd;

        public int FileLength { get; }

        public IReadOnlyList<ParsedPartIndex> Parts => _parts;
    }
}
