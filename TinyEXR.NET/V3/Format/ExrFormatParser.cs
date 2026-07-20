using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace TinyEXR.V3.Format
{
    /// <summary>
    /// Bounds-checked OpenEXR version, header, and offset-table parser.
    /// Pixel chunk bytes are deliberately outside this phase.
    /// </summary>
    internal static class ExrFormatParser
    {
        internal const uint Magic = 20_000_630U;
        internal const int SupportedFileVersion = 2;

        private const uint TiledFlag = 1U << 9;
        private const uint LongNamesFlag = 1U << 10;
        private const uint NonImageFlag = 1U << 11;
        private const uint MultipartFlag = 1U << 12;
        private const uint KnownVersionBits = 0xffU | TiledFlag | LongNamesFlag | NonImageFlag | MultipartFlag;

        private const int ShortNameByteLimit = 31;
        private const int LongNameByteLimit = 255;
        private const int MaximumAttributes = 1_048_576;
        private const int MaximumParts = 1_048_576;
        private const int MaximumAttributeByteCount = 256 * 1024 * 1024;
        private const long MaximumDimension = 1L << 20;
        private const int WindowCoordinateLimit = int.MaxValue / 2;

        private static readonly HashSet<string> ModeledAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "channels",
            "compression",
            "dataWindow",
            "displayWindow",
            "lineOrder",
            "pixelAspectRatio",
            "screenWindowCenter",
            "screenWindowWidth",
            "tiles",
            "name",
            "type",
            "chunkCount",
            "chromaticities",
        };

        internal static ExrResult Parse(ReadOnlyMemory<byte> source, out ParsedFile? parsedFile)
        {
            parsedFile = null;
            try
            {
                parsedFile = ParseCore(source.Span);
                return ExrResult.Success;
            }
            catch (FormatParseException exception)
            {
                return exception.Result;
            }
            catch (OutOfMemoryException)
            {
                return ExrResult.OutOfMemory;
            }
            catch (OverflowException)
            {
                return ExrResult.Corrupt;
            }
            catch (ArgumentException)
            {
                return ExrResult.Corrupt;
            }
        }

        /// <summary>
        /// Interprets a completed attribute list for the incremental reader without requiring
        /// a contiguous file image. Attribute arrays are already reader-owned and are not copied.
        /// </summary>
        internal static ExrResult InterpretHeaderAttributes(
            int partIndex,
            IReadOnlyList<HeaderAttribute> attributes,
            ParsedFileFlags flags,
            out Header? header,
            out int chunkCount)
        {
            header = null;
            chunkCount = 0;
            try
            {
                List<ParsedAttribute> rawAttributes = new List<ParsedAttribute>(attributes.Count);
                for (int i = 0; i < attributes.Count; i++)
                {
                    rawAttributes.Add(new ParsedAttribute(attributes[i], 0, 0, 0));
                }

                PartDraft draft = InterpretHeader(partIndex, rawAttributes, 0, 0, flags);
                header = draft.Header;
                chunkCount = draft.ChunkCount;
                return ExrResult.Success;
            }
            catch (FormatParseException exception)
            {
                return exception.Result;
            }
            catch (OutOfMemoryException)
            {
                return ExrResult.OutOfMemory;
            }
            catch (OverflowException)
            {
                return ExrResult.Corrupt;
            }
            catch (ArgumentException)
            {
                return ExrResult.Corrupt;
            }
        }

        internal static ExrResult ValidateHeaders(
            IReadOnlyList<Header> headers,
            ParsedFileFlags flags)
        {
            try
            {
                ValidateFileFlagsAndPartNames(headers, flags);
                return ExrResult.Success;
            }
            catch (FormatParseException exception)
            {
                return exception.Result;
            }
            catch (ArgumentException)
            {
                return ExrResult.Corrupt;
            }
        }

        internal static ExrResult InterpretVersionField(
            uint versionField,
            out ParsedFileFlags flags,
            out int nameByteLimit)
        {
            flags = default;
            nameByteLimit = 0;
            int fileVersion = (int)(versionField & 0xffU);
            if (fileVersion > SupportedFileVersion)
            {
                return ExrResult.Unsupported;
            }

            if (fileVersion != SupportedFileVersion)
            {
                return ExrResult.InvalidFile;
            }

            if ((versionField & ~KnownVersionBits) != 0)
            {
                return ExrResult.Unsupported;
            }

            flags = new ParsedFileFlags(
                (versionField & TiledFlag) != 0,
                (versionField & LongNamesFlag) != 0,
                (versionField & NonImageFlag) != 0,
                (versionField & MultipartFlag) != 0);
            if (flags.Multipart && flags.Tiled)
            {
                return ExrResult.Corrupt;
            }

            nameByteLimit = flags.LongNames ? LongNameByteLimit : ShortNameByteLimit;
            return ExrResult.Success;
        }

        private static ParsedFile ParseCore(ReadOnlySpan<byte> source)
        {
            if (source.Length < 8)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(source) != Magic)
            {
                throw Failure(ExrResult.InvalidFile);
            }

            uint versionField = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4));
            ExrResult versionResult = InterpretVersionField(versionField, out ParsedFileFlags flags, out int nameLimit);
            if (versionResult != ExrResult.Success)
            {
                throw Failure(versionResult);
            }

            Cursor cursor = new Cursor(source, 8);
            List<PartDraft> drafts = new List<PartDraft>();
            for (;;)
            {
                if (flags.Multipart && cursor.PeekByte() == 0)
                {
                    cursor.Skip(1);
                    break;
                }

                if (drafts.Count >= MaximumParts)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                int headerStart = cursor.Position;
                List<ParsedAttribute> rawAttributes = ParseAttributeList(ref cursor, nameLimit);
                int headerEnd = cursor.Position;
                drafts.Add(InterpretHeader(
                    drafts.Count,
                    rawAttributes,
                    headerStart,
                    headerEnd,
                    flags));

                if (!flags.Multipart)
                {
                    break;
                }
            }

            if (drafts.Count == 0)
            {
                throw Failure(ExrResult.Corrupt);
            }

            List<Header> parsedHeaders = new List<Header>(drafts.Count);
            for (int i = 0; i < drafts.Count; i++)
            {
                parsedHeaders.Add(drafts[i].Header);
            }

            ValidateFileFlagsAndPartNames(parsedHeaders, flags);

            int headersEnd = cursor.Position;
            int offsetTablesStart = headersEnd;
            long totalOffsetTableBytes = 0;
            for (int i = 0; i < drafts.Count; i++)
            {
                totalOffsetTableBytes = checked(totalOffsetTableBytes + checked((long)drafts[i].ChunkCount * sizeof(ulong)));
            }

            if (totalOffsetTableBytes > cursor.Remaining)
            {
                throw Failure(ExrResult.Corrupt);
            }

            for (int i = 0; i < drafts.Count; i++)
            {
                PartDraft draft = drafts[i];
                draft.OffsetTableStart = cursor.Position;
                draft.Offsets = new ulong[draft.ChunkCount];
                for (int chunk = 0; chunk < draft.Offsets.Length; chunk++)
                {
                    draft.Offsets[chunk] = cursor.ReadUInt64();
                }

                draft.OffsetTableEnd = cursor.Position;
            }

            int offsetTablesEnd = cursor.Position;
            List<ParsedPartIndex> parts = new List<ParsedPartIndex>(drafts.Count);
            for (int i = 0; i < drafts.Count; i++)
            {
                PartDraft draft = drafts[i];
                ParsedBlockGeometry[] geometries = BuildBlockGeometries(draft.Header, draft.ChunkCount, flags.Multipart);
                ParsedChunkIndex[] chunks = new ParsedChunkIndex[draft.ChunkCount];
                for (int chunk = 0; chunk < chunks.Length; chunk++)
                {
                    ulong offset = draft.Offsets![chunk];
                    int chunkHeaderByteCount = geometries[chunk].ChunkHeaderByteCount;
                    if (offset != 0 &&
                        (offset < (ulong)offsetTablesEnd ||
                         offset > (ulong)source.Length ||
                         (ulong)chunkHeaderByteCount > (ulong)source.Length - offset))
                    {
                        throw Failure(ExrResult.Corrupt);
                    }

                    chunks[chunk] = new ParsedChunkIndex(chunk, offset, geometries[chunk]);
                }

                parts.Add(new ParsedPartIndex(
                    draft.PartIndex,
                    draft.Header,
                    draft.RawAttributes,
                    draft.DeclaredChunkCount,
                    draft.HeaderStart,
                    draft.HeaderEnd,
                    draft.OffsetTableStart,
                    draft.OffsetTableEnd,
                    chunks));
            }

            return new ParsedFile(
                versionField,
                SupportedFileVersion,
                flags,
                headersStart: 8,
                headersEnd,
                offsetTablesStart,
                offsetTablesEnd,
                source.Length,
                parts);
        }

        private static List<ParsedAttribute> ParseAttributeList(ref Cursor cursor, int nameLimit)
        {
            List<ParsedAttribute> attributes = new List<ParsedAttribute>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (;;)
            {
                if (cursor.PeekByte() == 0)
                {
                    cursor.Skip(1);
                    break;
                }

                if (attributes.Count >= MaximumAttributes)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                int attributeStart = cursor.Position;
                string name = cursor.ReadCString(nameLimit, allowEmpty: false);
                string typeName = cursor.ReadCString(nameLimit, allowEmpty: false);
                if (!names.Add(name))
                {
                    throw Failure(ExrResult.Corrupt);
                }

                int byteCount = cursor.ReadInt32();
                if (byteCount < 0 || byteCount > MaximumAttributeByteCount)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                int dataStart = cursor.Position;
                byte[] data = cursor.ReadBytes(byteCount).ToArray();
                HeaderAttribute value = HeaderAttribute.Adopt(name, typeName, data);
                attributes.Add(new ParsedAttribute(value, attributeStart, dataStart, cursor.Position));
            }

            return attributes;
        }

        private static PartDraft InterpretHeader(
            int partIndex,
            List<ParsedAttribute> rawAttributes,
            int headerStart,
            int headerEnd,
            ParsedFileFlags flags)
        {
            Dictionary<string, ParsedAttribute> attributes = new Dictionary<string, ParsedAttribute>(rawAttributes.Count, StringComparer.Ordinal);
            for (int i = 0; i < rawAttributes.Count; i++)
            {
                attributes.Add(rawAttributes[i].Value.Name, rawAttributes[i]);
            }

            List<Channel> channels = ParseChannels(
                Required(attributes, "channels", "chlist").Value.Data,
                flags.LongNames ? LongNameByteLimit : ShortNameByteLimit);
            Compression compression = ParseCompression(Required(attributes, "compression", "compression", 1));
            Box2i dataWindow = ParseBox(Required(attributes, "dataWindow", "box2i", 16));
            Box2i displayWindow = ParseBox(Required(attributes, "displayWindow", "box2i", 16));
            ValidateWindowEndpoints(dataWindow);
            ValidateWindowEndpoints(displayWindow);
            if (dataWindow.Width > MaximumDimension || dataWindow.Height > MaximumDimension)
            {
                throw Failure(ExrResult.Corrupt);
            }

            LineOrder lineOrder = ParseLineOrder(Required(attributes, "lineOrder", "lineOrder", 1));
            float pixelAspectRatio = ParseSingle(Required(attributes, "pixelAspectRatio", "float", 4));
            Vector2 screenWindowCenter = ParseVector2(Required(attributes, "screenWindowCenter", "v2f", 8));
            float screenWindowWidth = ParseSingle(Required(attributes, "screenWindowWidth", "float", 4));

            PartType partType = ParsePartType(attributes, flags);
            bool tiled = partType == PartType.Tiled || partType == PartType.DeepTiled;
            bool deep = partType == PartType.DeepScanline || partType == PartType.DeepTiled;
            TileDescription? tiles = null;
            if (tiled)
            {
                tiles = ParseTileDescription(Required(attributes, "tiles", "tiledesc", 9));
            }

            string name = string.Empty;
            ParsedAttribute? nameAttribute = Optional(attributes, "name", "string");
            if (nameAttribute != null)
            {
                name = DecodeStringValue(nameAttribute, allowEmpty: true);
            }

            if (lineOrder == LineOrder.RandomY && !tiled)
            {
                throw Failure(ExrResult.Corrupt);
            }

            if (tiled || deep)
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    if (channels[i].XSampling != 1 || channels[i].YSampling != 1)
                    {
                        throw Failure(ExrResult.Corrupt);
                    }
                }
            }
            else
            {
                ValidateFlatScanlineSampling(dataWindow, channels);
            }

            uint? declaredChunkCount = null;
            ParsedAttribute? chunkCountAttribute = Optional(attributes, "chunkCount", "int", 4);
            if (chunkCountAttribute != null)
            {
                int signedCount = ReadInt32(chunkCountAttribute.Value.Data);
                if (signedCount < 0)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                declaredChunkCount = (uint)signedCount;
            }

            if (flags.Multipart && declaredChunkCount == null)
            {
                throw Failure(ExrResult.Corrupt);
            }

            if (flags.Multipart && name.Length == 0)
            {
                throw Failure(ExrResult.Corrupt);
            }

            if (deep)
            {
                ParsedAttribute version = Required(attributes, "version", "int", 4);
                if (ReadInt32(version.Value.Data) != 1)
                {
                    throw Failure(ExrResult.Unsupported);
                }

                ParsedAttribute? maxSamples = Optional(attributes, "maxSamplesPerPixel", "int", 4);
                if (maxSamples != null && ReadInt32(maxSamples.Value.Data) < -1)
                {
                    throw Failure(ExrResult.Corrupt);
                }
            }

            Chromaticities? chromaticities = null;
            ParsedAttribute? chromaticitiesAttribute = Optional(attributes, "chromaticities", "chromaticities", 32);
            if (chromaticitiesAttribute != null)
            {
                chromaticities = ParseChromaticities(chromaticitiesAttribute.Value.Data);
            }

            List<HeaderAttribute> customAttributes = new List<HeaderAttribute>();
            for (int i = 0; i < rawAttributes.Count; i++)
            {
                HeaderAttribute value = rawAttributes[i].Value;
                bool deepStandard = deep &&
                    (string.Equals(value.Name, "version", StringComparison.Ordinal) ||
                     string.Equals(value.Name, "maxSamplesPerPixel", StringComparison.Ordinal));
                if (!ModeledAttributeNames.Contains(value.Name) && !deepStandard)
                {
                    customAttributes.Add(value);
                }
            }

            Header header = new Header(
                partType,
                dataWindow,
                channels,
                compression,
                lineOrder,
                displayWindow,
                pixelAspectRatio,
                screenWindowCenter,
                screenWindowWidth,
                tiles,
                name,
                chromaticities,
                customAttributes);

            int computedChunkCount = ComputeChunkCount(header);
            if (declaredChunkCount != null && declaredChunkCount.Value != (uint)computedChunkCount)
            {
                throw Failure(ExrResult.Corrupt);
            }

            return new PartDraft(
                partIndex,
                header,
                rawAttributes,
                declaredChunkCount,
                computedChunkCount,
                headerStart,
                headerEnd);
        }

        private static void ValidateFileFlagsAndPartNames(
            IReadOnlyList<Header> headers,
            ParsedFileFlags flags)
        {
            bool anyDeep = false;
            HashSet<string> partNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < headers.Count; i++)
            {
                Header header = headers[i];
                anyDeep |= header.IsDeep;
                if (flags.Multipart && !partNames.Add(header.Name))
                {
                    throw Failure(ExrResult.Corrupt);
                }
            }

            if (flags.NonImage != anyDeep)
            {
                throw Failure(ExrResult.Corrupt);
            }

            if (!flags.Multipart)
            {
                bool requiresTiledFlag = headers[0].PartType == PartType.Tiled;
                if (flags.Tiled != requiresTiledFlag)
                {
                    throw Failure(ExrResult.Corrupt);
                }
            }
        }

        private static List<Channel> ParseChannels(ReadOnlySpan<byte> data, int nameLimit)
        {
            Cursor cursor = new Cursor(data, 0);
            List<Channel> channels = new List<Channel>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            string? previousName = null;
            for (;;)
            {
                if (cursor.PeekByte() == 0)
                {
                    cursor.Skip(1);
                    break;
                }

                string name = cursor.ReadCString(nameLimit, allowEmpty: false);
                int pixelTypeValue = cursor.ReadInt32();
                if (pixelTypeValue < 0)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                if (pixelTypeValue > (int)PixelType.Float)
                {
                    throw Failure(ExrResult.Unsupported);
                }

                byte pLinear = cursor.ReadByte();
                if (pLinear > 1 || cursor.ReadByte() != 0 || cursor.ReadByte() != 0 || cursor.ReadByte() != 0)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                int xSampling = cursor.ReadInt32();
                int ySampling = cursor.ReadInt32();
                if (xSampling <= 0 || ySampling <= 0 || !names.Add(name) ||
                    (previousName != null && ModelValidation.Utf8ByteComparer.Compare(previousName, name) >= 0))
                {
                    throw Failure(ExrResult.Corrupt);
                }

                channels.Add(new Channel(name, (PixelType)pixelTypeValue, xSampling, ySampling, pLinear != 0));
                previousName = name;
            }

            if (channels.Count == 0 || cursor.Remaining != 0)
            {
                throw Failure(ExrResult.Corrupt);
            }

            return channels;
        }

        private static Compression ParseCompression(ParsedAttribute attribute)
        {
            byte value = attribute.Value.Data[0];
            if (value > (byte)Compression.ZSTD)
            {
                throw Failure(ExrResult.Unsupported);
            }

            return (Compression)value;
        }

        private static LineOrder ParseLineOrder(ParsedAttribute attribute)
        {
            byte value = attribute.Value.Data[0];
            if (value > (byte)LineOrder.RandomY)
            {
                throw Failure(ExrResult.Corrupt);
            }

            return (LineOrder)value;
        }

        private static Box2i ParseBox(ParsedAttribute attribute)
        {
            ReadOnlySpan<byte> data = attribute.Value.Data;
            return new Box2i(
                ReadInt32(data),
                ReadInt32(data.Slice(4)),
                ReadInt32(data.Slice(8)),
                ReadInt32(data.Slice(12)));
        }

        private static Vector2 ParseVector2(ParsedAttribute attribute)
        {
            ReadOnlySpan<byte> data = attribute.Value.Data;
            return new Vector2(ReadSingle(data), ReadSingle(data.Slice(4)));
        }

        private static Chromaticities ParseChromaticities(ReadOnlySpan<byte> data)
        {
            return new Chromaticities(
                ReadSingle(data),
                ReadSingle(data.Slice(4)),
                ReadSingle(data.Slice(8)),
                ReadSingle(data.Slice(12)),
                ReadSingle(data.Slice(16)),
                ReadSingle(data.Slice(20)),
                ReadSingle(data.Slice(24)),
                ReadSingle(data.Slice(28)));
        }

        private static TileDescription ParseTileDescription(ParsedAttribute attribute)
        {
            ReadOnlySpan<byte> data = attribute.Value.Data;
            uint tileSizeX = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint tileSizeY = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
            byte mode = data[8];
            int levelMode = mode & 0x0f;
            int roundingMode = mode >> 4;
            if (tileSizeX == 0 || tileSizeX > int.MaxValue ||
                tileSizeY == 0 || tileSizeY > int.MaxValue ||
                levelMode > (int)TileLevelMode.RipmapLevels ||
                roundingMode > (int)TileRoundingMode.RoundUp)
            {
                throw Failure(ExrResult.Corrupt);
            }

            return new TileDescription(
                tileSizeX,
                tileSizeY,
                (TileLevelMode)levelMode,
                (TileRoundingMode)roundingMode);
        }

        private static PartType ParsePartType(
            Dictionary<string, ParsedAttribute> attributes,
            ParsedFileFlags flags)
        {
            ParsedAttribute? typeAttribute = Optional(attributes, "type", "string");
            PartType partType;
            if (typeAttribute == null)
            {
                if (flags.Multipart || flags.NonImage)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                partType = flags.Tiled ? PartType.Tiled : PartType.Scanline;
            }
            else
            {
                string type = DecodeStringValue(typeAttribute, allowEmpty: false);
                switch (type)
                {
                    case "scanlineimage":
                        partType = PartType.Scanline;
                        break;
                    case "tiledimage":
                        partType = PartType.Tiled;
                        break;
                    case "deepscanline":
                        partType = PartType.DeepScanline;
                        break;
                    case "deeptile":
                        partType = PartType.DeepTiled;
                        break;
                    default:
                        throw Failure(ExrResult.Unsupported);
                }
            }

            return partType;
        }

        private static void ValidateWindowEndpoints(Box2i window)
        {
            if (window.MinX <= -WindowCoordinateLimit ||
                window.MinY <= -WindowCoordinateLimit ||
                window.MaxX >= WindowCoordinateLimit ||
                window.MaxY >= WindowCoordinateLimit)
            {
                throw Failure(ExrResult.Corrupt);
            }
        }

        private static void ValidateFlatScanlineSampling(Box2i dataWindow, List<Channel> channels)
        {
            long minimumX = dataWindow.MinX;
            long minimumY = dataWindow.MinY;
            long width = dataWindow.Width;
            long height = dataWindow.Height;
            for (int i = 0; i < channels.Count; i++)
            {
                Channel channel = channels[i];
                long xSampling = channel.XSampling;
                long ySampling = channel.YSampling;
                if (minimumX % xSampling != 0 ||
                    minimumY % ySampling != 0 ||
                    width % xSampling != 0 ||
                    height % ySampling != 0)
                {
                    throw Failure(ExrResult.Corrupt);
                }
            }
        }

        internal static int ComputeChunkCount(Header header)
        {
            ulong count = 0;
            if (!header.IsTiled)
            {
                int linesPerBlock = LinesPerBlock(header.Compression);
                count = checked(((ulong)header.DataWindow.Height + (uint)linesPerBlock - 1UL) / (uint)linesPerBlock);
            }
            else
            {
                foreach (LevelLayout level in EnumerateLevels(header))
                {
                    count = checked(count + checked((ulong)level.TilesX * (ulong)level.TilesY));
                }
            }

            if (count == 0 || count > int.MaxValue)
            {
                throw Failure(ExrResult.Corrupt);
            }

            return (int)count;
        }

        private static ParsedBlockGeometry[] BuildBlockGeometries(Header header, int chunkCount, bool multipart)
        {
            ParsedBlockGeometry[] geometries = new ParsedBlockGeometry[chunkCount];
            int index = 0;
            int chunkHeaderByteCount = ChunkHeaderByteCount(header, multipart);
            if (!header.IsTiled)
            {
                int linesPerBlock = LinesPerBlock(header.Compression);
                for (; index < chunkCount; index++)
                {
                    long y0 = checked((long)header.DataWindow.MinY + checked((long)index * linesPerBlock));
                    int height = (int)Math.Min(linesPerBlock, checked((long)header.DataWindow.MaxY - y0 + 1L));
                    if (height <= 0)
                    {
                        throw Failure(ExrResult.Corrupt);
                    }

                    Box2i region = new Box2i(
                        header.DataWindow.MinX,
                        checked((int)y0),
                        header.DataWindow.MaxX,
                        checked((int)(y0 + height - 1L)));
                    geometries[index] = new ParsedBlockGeometry(
                        isTiled: false,
                        header.IsDeep,
                        levelX: 0,
                        levelY: 0,
                        tileX: -1,
                        tileY: -1,
                        region,
                        chunkHeaderByteCount,
                        header.IsDeep ? null : ComputeUncompressedByteCount(header, region));
                }
            }
            else
            {
                TileDescription tiles = header.Tiles!;
                foreach (LevelLayout level in EnumerateLevels(header))
                {
                    for (int tileY = 0; tileY < level.TilesY; tileY++)
                    {
                        for (int tileX = 0; tileX < level.TilesX; tileX++)
                        {
                            if (index >= geometries.Length)
                            {
                                throw Failure(ExrResult.Corrupt);
                            }

                            ulong relativeX = checked((ulong)tileX * tiles.TileSizeX);
                            ulong relativeY = checked((ulong)tileY * tiles.TileSizeY);
                            long width = (long)Math.Min((ulong)level.Width - relativeX, tiles.TileSizeX);
                            long height = (long)Math.Min((ulong)level.Height - relativeY, tiles.TileSizeY);
                            long minX = checked((long)header.DataWindow.MinX + (long)relativeX);
                            long minY = checked((long)header.DataWindow.MinY + (long)relativeY);
                            Box2i region = new Box2i(
                                checked((int)minX),
                                checked((int)minY),
                                checked((int)(minX + width - 1L)),
                                checked((int)(minY + height - 1L)));
                            geometries[index] = new ParsedBlockGeometry(
                                isTiled: true,
                                header.IsDeep,
                                level.LevelX,
                                level.LevelY,
                                tileX,
                                tileY,
                                region,
                                chunkHeaderByteCount,
                                header.IsDeep ? null : ComputeUncompressedByteCount(header, region));
                            index++;
                        }
                    }
                }

                if (index != geometries.Length)
                {
                    throw Failure(ExrResult.Corrupt);
                }
            }

            return geometries;
        }

        private static IEnumerable<LevelLayout> EnumerateLevels(Header header)
        {
            TileDescription tiles = header.Tiles!;
            int xLevels = LevelCount(header.DataWindow.Width, tiles.RoundingMode);
            int yLevels = LevelCount(header.DataWindow.Height, tiles.RoundingMode);
            if (tiles.LevelMode == TileLevelMode.OneLevel)
            {
                yield return CreateLevel(header, 0, 0);
            }
            else if (tiles.LevelMode == TileLevelMode.MipmapLevels)
            {
                int levels = Math.Max(xLevels, yLevels);
                for (int level = 0; level < levels; level++)
                {
                    yield return CreateLevel(header, level, level);
                }
            }
            else
            {
                for (int levelY = 0; levelY < yLevels; levelY++)
                {
                    for (int levelX = 0; levelX < xLevels; levelX++)
                    {
                        yield return CreateLevel(header, levelX, levelY);
                    }
                }
            }
        }

        private static LevelLayout CreateLevel(Header header, int levelX, int levelY)
        {
            TileDescription tiles = header.Tiles!;
            long width = LevelSize(header.DataWindow.Width, levelX, tiles.RoundingMode);
            long height = LevelSize(header.DataWindow.Height, levelY, tiles.RoundingMode);
            int tilesX = checked((int)CeilingDivide((ulong)width, tiles.TileSizeX));
            int tilesY = checked((int)CeilingDivide((ulong)height, tiles.TileSizeY));
            return new LevelLayout(levelX, levelY, width, height, tilesX, tilesY);
        }

        internal static int LevelCount(long size, TileRoundingMode roundingMode)
        {
            int count = 1;
            while (size > 1)
            {
                size = NextLevelSize(size, roundingMode);
                count++;
            }

            return count;
        }

        internal static long LevelSize(long size, int level, TileRoundingMode roundingMode)
        {
            for (int i = 0; i < level && size > 1; i++)
            {
                size = NextLevelSize(size, roundingMode);
            }

            return Math.Max(1L, size);
        }

        private static long NextLevelSize(long size, TileRoundingMode roundingMode)
        {
            return roundingMode == TileRoundingMode.RoundUp
                ? (size / 2L) + (size & 1L)
                : Math.Max(1L, size / 2L);
        }

        private static ulong CeilingDivide(ulong value, uint divisor)
        {
            return (value + divisor - 1UL) / divisor;
        }

        internal static ulong ComputeUncompressedByteCount(Header header, Box2i region)
        {
            ulong total = 0;
            for (int i = 0; i < header.Channels.Count; i++)
            {
                Channel channel = header.Channels[i];
                ulong samples = ModelValidation.CountSamples(region, channel.XSampling, channel.YSampling);
                total = checked(total + checked(samples * (uint)ModelValidation.PixelTypeSize(channel.PixelType)));
            }

            return total;
        }

        internal static int ChunkHeaderByteCount(Header header, bool multipart)
        {
            int baseSize;
            if (header.IsDeep)
            {
                baseSize = header.IsTiled ? 40 : 28;
            }
            else
            {
                baseSize = header.IsTiled ? 20 : 8;
            }

            return multipart ? baseSize + sizeof(int) : baseSize;
        }

        internal static int LinesPerBlock(Compression compression)
        {
            switch (compression)
            {
                case Compression.None:
                case Compression.RLE:
                case Compression.ZIPS:
                    return 1;
                case Compression.ZIP:
                case Compression.PXR24:
                    return 16;
                case Compression.PIZ:
                case Compression.B44:
                case Compression.B44A:
                case Compression.DWAA:
                case Compression.HTJ2K32:
                case Compression.ZSTD:
                    return 32;
                case Compression.DWAB:
                case Compression.HTJ2K256:
                    return 256;
                default:
                    throw Failure(ExrResult.Unsupported);
            }
        }

        private static ParsedAttribute Required(
            Dictionary<string, ParsedAttribute> attributes,
            string name,
            string typeName,
            int? byteCount = null)
        {
            if (!attributes.TryGetValue(name, out ParsedAttribute? attribute))
            {
                throw Failure(ExrResult.Corrupt);
            }

            ValidateAttribute(attribute, typeName, byteCount);
            return attribute;
        }

        private static ParsedAttribute? Optional(
            Dictionary<string, ParsedAttribute> attributes,
            string name,
            string typeName,
            int? byteCount = null)
        {
            if (!attributes.TryGetValue(name, out ParsedAttribute? attribute))
            {
                return null;
            }

            ValidateAttribute(attribute, typeName, byteCount);
            return attribute;
        }

        private static void ValidateAttribute(ParsedAttribute attribute, string typeName, int? byteCount)
        {
            if (!string.Equals(attribute.Value.TypeName, typeName, StringComparison.Ordinal) ||
                (byteCount != null && attribute.Value.ByteLength != byteCount.Value))
            {
                throw Failure(ExrResult.Corrupt);
            }
        }

        private static string DecodeStringValue(ParsedAttribute attribute, bool allowEmpty)
        {
            ReadOnlySpan<byte> data = attribute.Value.Data;
            if ((!allowEmpty && data.Length == 0) || data.IndexOf((byte)0) >= 0)
            {
                throw Failure(ExrResult.Corrupt);
            }

            try
            {
                return ModelValidation.StrictUtf8.GetString(data.ToArray());
            }
            catch (DecoderFallbackException)
            {
                throw Failure(ExrResult.Corrupt);
            }
        }

        private static float ParseSingle(ParsedAttribute attribute)
        {
            return ReadSingle(attribute.Value.Data);
        }

        private static int ReadInt32(ReadOnlySpan<byte> data)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(data);
        }

        private static float ReadSingle(ReadOnlySpan<byte> data)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
        }

        private static FormatParseException Failure(ExrResult result)
        {
            return new FormatParseException(result);
        }

        private sealed class PartDraft
        {
            public PartDraft(
                int partIndex,
                Header header,
                List<ParsedAttribute> rawAttributes,
                uint? declaredChunkCount,
                int chunkCount,
                int headerStart,
                int headerEnd)
            {
                PartIndex = partIndex;
                Header = header;
                RawAttributes = rawAttributes;
                DeclaredChunkCount = declaredChunkCount;
                ChunkCount = chunkCount;
                HeaderStart = headerStart;
                HeaderEnd = headerEnd;
            }

            public int PartIndex { get; }

            public Header Header { get; }

            public List<ParsedAttribute> RawAttributes { get; }

            public uint? DeclaredChunkCount { get; }

            public int ChunkCount { get; }

            public int HeaderStart { get; }

            public int HeaderEnd { get; }

            public int OffsetTableStart { get; set; }

            public int OffsetTableEnd { get; set; }

            public ulong[]? Offsets { get; set; }
        }

        private readonly struct LevelLayout
        {
            public LevelLayout(int levelX, int levelY, long width, long height, int tilesX, int tilesY)
            {
                LevelX = levelX;
                LevelY = levelY;
                Width = width;
                Height = height;
                TilesX = tilesX;
                TilesY = tilesY;
            }

            public int LevelX { get; }

            public int LevelY { get; }

            public long Width { get; }

            public long Height { get; }

            public int TilesX { get; }

            public int TilesY { get; }
        }

        private sealed class FormatParseException : Exception
        {
            public FormatParseException(ExrResult result)
            {
                Result = result;
            }

            public ExrResult Result { get; }
        }

        private ref struct Cursor
        {
            private readonly ReadOnlySpan<byte> _source;

            public Cursor(ReadOnlySpan<byte> source, int position)
            {
                _source = source;
                Position = position;
            }

            public int Position { get; private set; }

            public int Remaining => _source.Length - Position;

            public byte PeekByte()
            {
                Require(1);
                return _source[Position];
            }

            public byte ReadByte()
            {
                Require(1);
                return _source[Position++];
            }

            public int ReadInt32()
            {
                Require(sizeof(int));
                int value = BinaryPrimitives.ReadInt32LittleEndian(_source.Slice(Position, sizeof(int)));
                Position += sizeof(int);
                return value;
            }

            public ulong ReadUInt64()
            {
                Require(sizeof(ulong));
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_source.Slice(Position, sizeof(ulong)));
                Position += sizeof(ulong);
                return value;
            }

            public ReadOnlySpan<byte> ReadBytes(int byteCount)
            {
                Require(byteCount);
                ReadOnlySpan<byte> result = _source.Slice(Position, byteCount);
                Position += byteCount;
                return result;
            }

            public string ReadCString(int byteLimit, bool allowEmpty)
            {
                ReadOnlySpan<byte> remaining = _source.Slice(Position);
                int length = remaining.IndexOf((byte)0);
                if (length < 0 || length > byteLimit || (!allowEmpty && length == 0))
                {
                    throw Failure(ExrResult.Corrupt);
                }

                string value;
                try
                {
                    value = ModelValidation.StrictUtf8.GetString(remaining.Slice(0, length).ToArray());
                }
                catch (DecoderFallbackException)
                {
                    throw Failure(ExrResult.Corrupt);
                }

                Position += length + 1;
                return value;
            }

            public void Skip(int byteCount)
            {
                Require(byteCount);
                Position += byteCount;
            }

            private void Require(int byteCount)
            {
                if (byteCount < 0 || Position < 0 || Position > _source.Length || byteCount > _source.Length - Position)
                {
                    throw Failure(ExrResult.Corrupt);
                }
            }
        }
    }
}
