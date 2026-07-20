using System;
using System.Collections.Generic;
using TinyEXR.V3.Format;

namespace TinyEXR.V3
{
    internal sealed class ReaderFileData
    {
        public ReaderFileData(
            uint rawVersionField,
            bool multipart,
            long pixelDataStart,
            ReaderPartData[] parts)
        {
            RawVersionField = rawVersionField;
            Multipart = multipart;
            PixelDataStart = pixelDataStart;
            Parts = parts;
        }

        public uint RawVersionField { get; }

        public bool Multipart { get; }

        public long PixelDataStart { get; }

        public ReaderPartData[] Parts { get; }
    }

    internal sealed class ReaderPartData
    {
        public ReaderPartData(
            int partIndex,
            Header header,
            ulong[] offsets,
            ReaderPartLayout layout,
            long headerEnd,
            bool hasNameAttribute,
            bool hasTypeAttribute,
            IReadOnlyList<HeaderAttribute> rawAttributes)
        {
            PartIndex = partIndex;
            Header = header;
            Offsets = offsets;
            Layout = layout;
            HeaderEnd = headerEnd;
            HasNameAttribute = hasNameAttribute;
            HasTypeAttribute = hasTypeAttribute;
            RawAttributes = rawAttributes ?? throw new ArgumentNullException(nameof(rawAttributes));
        }

        public int PartIndex { get; }

        public Header Header { get; }

        public ulong[] Offsets { get; }

        public ReaderPartLayout Layout { get; }

        public long HeaderEnd { get; }

        public bool HasNameAttribute { get; }

        public bool HasTypeAttribute { get; }

        public IReadOnlyList<HeaderAttribute> RawAttributes { get; }

        public BlockInfo GetBlockInfo(int blockIndex)
        {
            ulong storedOffset = Offsets[blockIndex];
            return Layout.GetBlockInfo(
                PartIndex,
                blockIndex,
                storedOffset == 0 ? 0L : checked((long)storedOffset));
        }
    }

    /// <summary>
    /// Compact logical index. Tiled parts retain one descriptor per level, never one object per block.
    /// </summary>
    internal sealed class ReaderPartLayout
    {
        private readonly Header _header;
        private readonly bool _multipart;
        private readonly LevelDescriptor[] _levels;

        private ReaderPartLayout(
            Header header,
            bool multipart,
            int blockCount,
            LevelDescriptor[] levels)
        {
            _header = header;
            _multipart = multipart;
            BlockCount = blockCount;
            _levels = levels;
        }

        public int BlockCount { get; }

        public int LevelDescriptorCount => _levels.Length;

        public int LevelCount => _header.IsTiled ? _levels.Length : 1;

        public static ReaderPartLayout Create(Header header, bool multipart, int expectedBlockCount)
        {
            if (!header.IsTiled)
            {
                int linesPerBlock = ExrFormatParser.LinesPerBlock(header.Compression);
                ulong count = checked(
                    ((ulong)header.DataWindow.Height + (uint)linesPerBlock - 1UL) /
                    (uint)linesPerBlock);
                if (count == 0 || count > int.MaxValue || (int)count != expectedBlockCount)
                {
                    throw new InvalidOperationException("The scanline block count is inconsistent with the header.");
                }

                return new ReaderPartLayout(header, multipart, expectedBlockCount, Array.Empty<LevelDescriptor>());
            }

            TileDescription tiles = header.Tiles!;
            int xLevels = ExrFormatParser.LevelCount(header.DataWindow.Width, tiles.RoundingMode);
            int yLevels = ExrFormatParser.LevelCount(header.DataWindow.Height, tiles.RoundingMode);
            List<LevelDescriptor> levels = new List<LevelDescriptor>();
            int blockBase = 0;

            if (tiles.LevelMode == TileLevelMode.OneLevel)
            {
                AddLevel(header, 0, 0, ref blockBase, levels);
            }
            else if (tiles.LevelMode == TileLevelMode.MipmapLevels)
            {
                int count = Math.Max(xLevels, yLevels);
                for (int level = 0; level < count; level++)
                {
                    AddLevel(header, level, level, ref blockBase, levels);
                }
            }
            else
            {
                for (int levelY = 0; levelY < yLevels; levelY++)
                {
                    for (int levelX = 0; levelX < xLevels; levelX++)
                    {
                        AddLevel(header, levelX, levelY, ref blockBase, levels);
                    }
                }
            }

            if (blockBase != expectedBlockCount)
            {
                throw new InvalidOperationException("The tiled block count is inconsistent with the header.");
            }

            return new ReaderPartLayout(header, multipart, expectedBlockCount, levels.ToArray());
        }

        public BlockInfo GetBlockInfo(int partIndex, int blockIndex, long fileOffset)
        {
            if (!_header.IsTiled)
            {
                int linesPerBlock = ExrFormatParser.LinesPerBlock(_header.Compression);
                long scanlineMinimumY = checked(
                    (long)_header.DataWindow.MinY + checked((long)blockIndex * linesPerBlock));
                int scanlineHeight = checked((int)Math.Min(
                    linesPerBlock,
                    checked((long)_header.DataWindow.MaxY - scanlineMinimumY + 1L)));
                Box2i region = new Box2i(
                    _header.DataWindow.MinX,
                    checked((int)scanlineMinimumY),
                    _header.DataWindow.MaxX,
                    checked((int)(scanlineMinimumY + scanlineHeight - 1L)));
                return CreateBlockInfo(
                    partIndex,
                    blockIndex,
                    levelX: 0,
                    levelY: 0,
                    tileX: -1,
                    tileY: -1,
                    region,
                    fileOffset);
            }

            LevelDescriptor level = FindLevel(blockIndex);
            int relativeIndex = blockIndex - level.BlockBase;
            int tileY = relativeIndex / level.TilesX;
            int tileX = relativeIndex % level.TilesX;
            TileDescription tiles = _header.Tiles!;
            ulong relativeX = checked((ulong)tileX * tiles.TileSizeX);
            ulong relativeY = checked((ulong)tileY * tiles.TileSizeY);
            long width = checked((long)Math.Min((ulong)level.Width - relativeX, tiles.TileSizeX));
            long height = checked((long)Math.Min((ulong)level.Height - relativeY, tiles.TileSizeY));
            long minimumX = checked((long)_header.DataWindow.MinX + (long)relativeX);
            long minimumY = checked((long)_header.DataWindow.MinY + (long)relativeY);
            Box2i tileRegion = new Box2i(
                checked((int)minimumX),
                checked((int)minimumY),
                checked((int)(minimumX + width - 1L)),
                checked((int)(minimumY + height - 1L)));
            return CreateBlockInfo(
                partIndex,
                blockIndex,
                level.LevelX,
                level.LevelY,
                tileX,
                tileY,
                tileRegion,
                fileOffset);
        }

        public bool TryGetScanlineBlockIndex(int minimumY, out int blockIndex)
        {
            blockIndex = -1;
            if (_header.IsTiled)
            {
                return false;
            }

            int linesPerBlock = ExrFormatParser.LinesPerBlock(_header.Compression);
            long relativeY = (long)minimumY - _header.DataWindow.MinY;
            if (relativeY < 0 || relativeY % linesPerBlock != 0)
            {
                return false;
            }

            long candidate = relativeY / linesPerBlock;
            if ((ulong)candidate >= (ulong)BlockCount)
            {
                return false;
            }

            blockIndex = (int)candidate;
            return true;
        }

        public bool TryGetTiledBlockIndex(
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            out int blockIndex)
        {
            blockIndex = -1;
            if (!_header.IsTiled || tileX < 0 || tileY < 0)
            {
                return false;
            }

            for (int i = 0; i < _levels.Length; i++)
            {
                LevelDescriptor level = _levels[i];
                if (level.LevelX != levelX || level.LevelY != levelY)
                {
                    continue;
                }

                if (tileX >= level.TilesX || tileY >= level.TilesY)
                {
                    return false;
                }

                blockIndex = checked(level.BlockBase + checked(tileY * level.TilesX) + tileX);
                return true;
            }

            return false;
        }

        public ReaderLevelInfo GetLevelInfo(int levelIndex)
        {
            if ((uint)levelIndex >= (uint)LevelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(levelIndex));
            }

            if (!_header.IsTiled)
            {
                return new ReaderLevelInfo(
                    0,
                    0,
                    _header.DataWindow,
                    0,
                    BlockCount);
            }

            LevelDescriptor level = _levels[levelIndex];
            Box2i region = new Box2i(
                _header.DataWindow.MinX,
                _header.DataWindow.MinY,
                checked((int)((long)_header.DataWindow.MinX + level.Width - 1L)),
                checked((int)((long)_header.DataWindow.MinY + level.Height - 1L)));
            return new ReaderLevelInfo(
                level.LevelX,
                level.LevelY,
                region,
                level.BlockBase,
                level.BlockEnd - level.BlockBase);
        }

        private BlockInfo CreateBlockInfo(
            int partIndex,
            int blockIndex,
            int levelX,
            int levelY,
            int tileX,
            int tileY,
            Box2i region,
            long fileOffset)
        {
            return new BlockInfo(
                partIndex,
                blockIndex,
                _header.IsTiled,
                _header.IsDeep,
                levelX,
                levelY,
                tileX,
                tileY,
                region,
                ExrFormatParser.ChunkHeaderByteCount(_header, _multipart),
                _header.IsDeep ? null : ExrFormatParser.ComputeUncompressedByteCount(_header, region),
                fileOffset);
        }

        private LevelDescriptor FindLevel(int blockIndex)
        {
            int low = 0;
            int high = _levels.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                LevelDescriptor candidate = _levels[middle];
                if (blockIndex < candidate.BlockBase)
                {
                    high = middle - 1;
                }
                else if (blockIndex >= candidate.BlockEnd)
                {
                    low = middle + 1;
                }
                else
                {
                    return candidate;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(blockIndex));
        }

        private static void AddLevel(
            Header header,
            int levelX,
            int levelY,
            ref int blockBase,
            List<LevelDescriptor> levels)
        {
            TileDescription tiles = header.Tiles!;
            long width = ExrFormatParser.LevelSize(header.DataWindow.Width, levelX, tiles.RoundingMode);
            long height = ExrFormatParser.LevelSize(header.DataWindow.Height, levelY, tiles.RoundingMode);
            int tilesX = checked((int)CeilingDivide((ulong)width, tiles.TileSizeX));
            int tilesY = checked((int)CeilingDivide((ulong)height, tiles.TileSizeY));
            int count = checked(tilesX * tilesY);
            levels.Add(new LevelDescriptor(
                levelX,
                levelY,
                width,
                height,
                tilesX,
                tilesY,
                blockBase,
                checked(blockBase + count)));
            blockBase = checked(blockBase + count);
        }

        private static ulong CeilingDivide(ulong value, uint divisor)
        {
            return checked(value + divisor - 1UL) / divisor;
        }

        private readonly struct LevelDescriptor
        {
            public LevelDescriptor(
                int levelX,
                int levelY,
                long width,
                long height,
                int tilesX,
                int tilesY,
                int blockBase,
                int blockEnd)
            {
                LevelX = levelX;
                LevelY = levelY;
                Width = width;
                Height = height;
                TilesX = tilesX;
                TilesY = tilesY;
                BlockBase = blockBase;
                BlockEnd = blockEnd;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public long Width { get; }

            public long Height { get; }

            public int TilesX { get; }

            public int TilesY { get; }

            public int BlockBase { get; }

            public int BlockEnd { get; }
        }
    }

    internal readonly struct ReaderLevelInfo
    {
        public ReaderLevelInfo(
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
}
