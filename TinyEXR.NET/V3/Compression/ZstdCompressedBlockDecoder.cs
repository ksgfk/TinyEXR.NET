using System.Buffers.Binary;

namespace TinyEXR.V3.Codecs
{
    internal sealed class ZstdCompressedBlockState
    {
        internal ZstdCompressedBlockState()
        {
            RepeatOffsets = new ulong[] { 1, 4, 8 };
        }

        internal ZstdHuffmanTable? HuffmanTable { get; set; }

        internal ZstdFseTable? LiteralLengthTable { get; set; }

        internal ZstdFseTable? OffsetTable { get; set; }

        internal ZstdFseTable? MatchLengthTable { get; set; }

        internal bool HasSequenceTables { get; set; }

        internal ulong[] RepeatOffsets { get; }
    }

    internal static class ZstdCompressedBlockDecoder
    {
        private static readonly uint[] LiteralLengthBases =
        {
            0, 1, 2, 3, 4, 5, 6, 7,
            8, 9, 10, 11, 12, 13, 14, 15,
            16, 18, 20, 22, 24, 28, 32, 40,
            48, 64, 0x80, 0x100, 0x200, 0x400, 0x800, 0x1000,
            0x2000, 0x4000, 0x8000, 0x10000,
        };

        private static readonly byte[] LiteralLengthBits =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 2, 2, 3, 3,
            4, 6, 7, 8, 9, 10, 11, 12,
            13, 14, 15, 16,
        };

        private static readonly uint[] MatchLengthBases =
        {
            3, 4, 5, 6, 7, 8, 9, 10,
            11, 12, 13, 14, 15, 16, 17, 18,
            19, 20, 21, 22, 23, 24, 25, 26,
            27, 28, 29, 30, 31, 32, 33, 34,
            35, 37, 39, 41, 43, 47, 51, 59,
            67, 83, 99, 0x83, 0x103, 0x203, 0x403, 0x803,
            0x1003, 0x2003, 0x4003, 0x8003, 0x10003,
        };

        private static readonly byte[] MatchLengthBits =
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 2, 2, 3, 3,
            4, 4, 5, 7, 8, 9, 10, 11,
            12, 13, 14, 15, 16,
        };

        private static readonly uint[] OffsetBases =
        {
            0, 1, 1, 5, 0x0d, 0x1d, 0x3d, 0x7d,
            0xfd, 0x1fd, 0x3fd, 0x7fd, 0xffd, 0x1ffd, 0x3ffd, 0x7ffd,
            0xfffd, 0x1fffd, 0x3fffd, 0x7fffd, 0xffffd, 0x1ffffd, 0x3ffffd, 0x7ffffd,
            0xfffffd, 0x1fffffd, 0x3fffffd, 0x7fffffd, 0xffffffd, 0x1ffffffd, 0x3ffffffd, 0x7ffffffd,
        };

        private static readonly byte[] OffsetBits =
        {
            0, 1, 2, 3, 4, 5, 6, 7,
            8, 9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23,
            24, 25, 26, 27, 28, 29, 30, 31,
        };

        private static readonly ZstdFseTable DefaultLiteralLengthTable = CreateDefaultTable(
            new short[]
            {
                4, 3, 2, 2, 2, 2, 2, 2,
                2, 2, 2, 2, 2, 1, 1, 1,
                2, 2, 2, 2, 2, 2, 2, 2,
                2, 3, 2, 1, 1, 1, 1, 1,
                -1, -1, -1, -1,
            },
            tableLog: 6);

        private static readonly ZstdFseTable DefaultOffsetTable = CreateDefaultTable(
            new short[]
            {
                1, 1, 1, 1, 1, 1, 2, 2,
                2, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1,
                -1, -1, -1, -1, -1,
            },
            tableLog: 5);

        private static readonly ZstdFseTable DefaultMatchLengthTable = CreateDefaultTable(
            new short[]
            {
                1, 4, 3, 2, 2, 2, 2, 2,
                2, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, -1, -1,
                -1, -1, -1, -1, -1,
            },
            tableLog: 6);

        internal static ZstdFrameStatus Decode(
            ReadOnlySpan<byte> source,
            Span<byte> frameOutput,
            int blockOutputOffset,
            int blockMaximumSize,
            ulong windowSize,
            bool hasExpectedFrameSize,
            ulong expectedFrameSize,
            ZstdCompressedBlockState state,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (blockOutputOffset < 0 || blockOutputOffset > frameOutput.Length || blockMaximumSize < 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            ZstdFrameStatus literalStatus = DecodeLiterals(
                source,
                blockMaximumSize,
                state,
                out byte[] literals,
                out int literalSectionSize);
            if (literalStatus != ZstdFrameStatus.Success)
            {
                return literalStatus;
            }

            if (literalSectionSize >= source.Length)
            {
                return ZstdFrameStatus.Truncated;
            }

            ReadOnlySpan<byte> sequenceSection = source.Slice(literalSectionSize);
            int sequenceOffset = 0;
            int sequenceCount = sequenceSection[sequenceOffset++];
            if (sequenceCount > 0x7f)
            {
                if (sequenceCount == 0xff)
                {
                    if (sequenceSection.Length - sequenceOffset < 2)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    sequenceCount = checked(BinaryPrimitives.ReadUInt16LittleEndian(sequenceSection.Slice(sequenceOffset, 2)) + 0x7f00);
                    sequenceOffset += 2;
                }
                else
                {
                    if (sequenceOffset >= sequenceSection.Length)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    sequenceCount = checked(((sequenceCount - 0x80) << 8) + sequenceSection[sequenceOffset++]);
                }
            }

            if (sequenceCount == 0)
            {
                if (sequenceOffset != sequenceSection.Length)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                return CopyLastLiterals(
                    literals,
                    frameOutput,
                    blockOutputOffset,
                    blockMaximumSize,
                    hasExpectedFrameSize,
                    expectedFrameSize,
                    out bytesWritten);
            }

            if (sequenceCount > checked((blockMaximumSize / 3) + 1) || sequenceOffset >= sequenceSection.Length)
            {
                return sequenceOffset >= sequenceSection.Length
                    ? ZstdFrameStatus.Truncated
                    : ZstdFrameStatus.Corrupt;
            }

            byte modes = sequenceSection[sequenceOffset++];
            if ((modes & 0x03) != 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int literalLengthMode = modes >> 6;
            int offsetMode = (modes >> 4) & 0x03;
            int matchLengthMode = (modes >> 2) & 0x03;

            ZstdFrameStatus llStatus = ReadSequenceTable(
                sequenceSection,
                ref sequenceOffset,
                literalLengthMode,
                maximumSymbol: 35,
                maximumTableLog: 9,
                DefaultLiteralLengthTable,
                state.LiteralLengthTable,
                state.HasSequenceTables,
                out ZstdFseTable? literalLengthTable);
            if (llStatus != ZstdFrameStatus.Success || literalLengthTable is null)
            {
                return llStatus;
            }

            ZstdFrameStatus offsetStatus = ReadSequenceTable(
                sequenceSection,
                ref sequenceOffset,
                offsetMode,
                maximumSymbol: 31,
                maximumTableLog: 8,
                DefaultOffsetTable,
                state.OffsetTable,
                state.HasSequenceTables,
                out ZstdFseTable? offsetTable);
            if (offsetStatus != ZstdFrameStatus.Success || offsetTable is null)
            {
                return offsetStatus;
            }

            ZstdFrameStatus mlStatus = ReadSequenceTable(
                sequenceSection,
                ref sequenceOffset,
                matchLengthMode,
                maximumSymbol: 52,
                maximumTableLog: 9,
                DefaultMatchLengthTable,
                state.MatchLengthTable,
                state.HasSequenceTables,
                out ZstdFseTable? matchLengthTable);
            if (mlStatus != ZstdFrameStatus.Success || matchLengthTable is null)
            {
                return mlStatus;
            }

            if (sequenceOffset >= sequenceSection.Length
                || !ZstdReverseBitReader.TryCreate(sequenceSection.Slice(sequenceOffset), out ZstdReverseBitReader reader))
            {
                return ZstdFrameStatus.Corrupt;
            }

            if (!ZstdFseTable.TryInitializeState(literalLengthTable, ref reader, out int literalLengthState)
                || !ZstdFseTable.TryInitializeState(offsetTable, ref reader, out int offsetState)
                || !ZstdFseTable.TryInitializeState(matchLengthTable, ref reader, out int matchLengthState))
            {
                return ZstdFrameStatus.Corrupt;
            }

            ulong repeat0 = state.RepeatOffsets[0];
            ulong repeat1 = state.RepeatOffsets[1];
            ulong repeat2 = state.RepeatOffsets[2];
            int literalOffset = 0;
            int outputOffset = blockOutputOffset;

            for (int sequenceIndex = 0; sequenceIndex < sequenceCount; sequenceIndex++)
            {
                if ((uint)literalLengthState >= (uint)literalLengthTable.Entries.Length
                    || (uint)offsetState >= (uint)offsetTable.Entries.Length
                    || (uint)matchLengthState >= (uint)matchLengthTable.Entries.Length)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                ZstdFseEntry literalLengthEntry = literalLengthTable.Entries[literalLengthState];
                ZstdFseEntry offsetEntry = offsetTable.Entries[offsetState];
                ZstdFseEntry matchLengthEntry = matchLengthTable.Entries[matchLengthState];
                int literalLengthCode = literalLengthEntry.Symbol;
                int offsetCode = offsetEntry.Symbol;
                int matchLengthCode = matchLengthEntry.Symbol;
                if ((uint)literalLengthCode >= (uint)LiteralLengthBases.Length
                    || (uint)offsetCode >= (uint)OffsetBases.Length
                    || (uint)matchLengthCode >= (uint)MatchLengthBases.Length)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                int literalAdditionalBits = LiteralLengthBits[literalLengthCode];
                int offsetAdditionalBits = OffsetBits[offsetCode];
                int matchAdditionalBits = MatchLengthBits[matchLengthCode];
                bool literalLengthIsZero = LiteralLengthBases[literalLengthCode] == 0;

                ulong offset;
                if (offsetAdditionalBits > 1)
                {
                    if (!reader.TryReadBits(offsetAdditionalBits, out uint offsetExtra))
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    offset = checked((ulong)OffsetBases[offsetCode] + offsetExtra);
                    repeat2 = repeat1;
                    repeat1 = repeat0;
                    repeat0 = offset;
                }
                else if (offsetAdditionalBits == 0)
                {
                    if (literalLengthIsZero)
                    {
                        offset = repeat1;
                        repeat1 = repeat0;
                        repeat0 = offset;
                    }
                    else
                    {
                        offset = repeat0;
                    }
                }
                else
                {
                    if (!reader.TryReadBits(1, out uint offsetExtra))
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    int repeatCode = checked((int)OffsetBases[offsetCode]
                        + (literalLengthIsZero ? 1 : 0)
                        + (int)offsetExtra);
                    ulong selected;
                    if (repeatCode == 3)
                    {
                        if (repeat0 <= 1)
                        {
                            return ZstdFrameStatus.Corrupt;
                        }

                        selected = repeat0 - 1;
                    }
                    else
                    {
                        selected = repeatCode switch
                        {
                            0 => repeat0,
                            1 => repeat1,
                            2 => repeat2,
                            _ => 0,
                        };
                    }

                    if (selected == 0)
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    if (repeatCode != 1)
                    {
                        repeat2 = repeat1;
                    }

                    repeat1 = repeat0;
                    repeat0 = selected;
                    offset = selected;
                }

                if (!reader.TryReadBits(matchAdditionalBits, out uint matchExtra)
                    || !reader.TryReadBits(literalAdditionalBits, out uint literalExtra))
                {
                    return ZstdFrameStatus.Corrupt;
                }

                ulong matchLength = checked((ulong)MatchLengthBases[matchLengthCode] + matchExtra);
                ulong literalLength = checked((ulong)LiteralLengthBases[literalLengthCode] + literalExtra);

                if (sequenceIndex != sequenceCount - 1)
                {
                    if (!ZstdFseTable.TryUpdateState(literalLengthTable, ref literalLengthState, ref reader)
                        || !ZstdFseTable.TryUpdateState(matchLengthTable, ref matchLengthState, ref reader)
                        || !ZstdFseTable.TryUpdateState(offsetTable, ref offsetState, ref reader))
                    {
                        return ZstdFrameStatus.Corrupt;
                    }
                }

                ZstdFrameStatus executeStatus = ExecuteSequence(
                    literals,
                    ref literalOffset,
                    literalLength,
                    matchLength,
                    offset,
                    frameOutput,
                    ref outputOffset,
                    blockOutputOffset,
                    blockMaximumSize,
                    windowSize,
                    hasExpectedFrameSize,
                    expectedFrameSize);
                if (executeStatus != ZstdFrameStatus.Success)
                {
                    return executeStatus;
                }
            }

            if (!reader.IsAtEnd)
            {
                return ZstdFrameStatus.Corrupt;
            }

            ZstdFrameStatus lastLiteralStatus = CopyRemainingLiterals(
                literals,
                literalOffset,
                frameOutput,
                ref outputOffset,
                blockOutputOffset,
                blockMaximumSize,
                hasExpectedFrameSize,
                expectedFrameSize);
            if (lastLiteralStatus != ZstdFrameStatus.Success)
            {
                return lastLiteralStatus;
            }

            state.LiteralLengthTable = literalLengthTable;
            state.OffsetTable = offsetTable;
            state.MatchLengthTable = matchLengthTable;
            state.HasSequenceTables = true;
            state.RepeatOffsets[0] = repeat0;
            state.RepeatOffsets[1] = repeat1;
            state.RepeatOffsets[2] = repeat2;
            bytesWritten = outputOffset - blockOutputOffset;
            return ZstdFrameStatus.Success;
        }

        private static ZstdFrameStatus DecodeLiterals(
            ReadOnlySpan<byte> source,
            int blockMaximumSize,
            ZstdCompressedBlockState state,
            out byte[] literals,
            out int bytesConsumed)
        {
            literals = Array.Empty<byte>();
            bytesConsumed = 0;
            if (source.IsEmpty)
            {
                return ZstdFrameStatus.Truncated;
            }

            int literalType = source[0] & 0x03;
            int sizeFormat = (source[0] >> 2) & 0x03;
            if (literalType <= 1)
            {
                int headerSize;
                int regeneratedSize;
                switch (sizeFormat)
                {
                    case 0:
                    case 2:
                        headerSize = 1;
                        regeneratedSize = source[0] >> 3;
                        break;
                    case 1:
                        if (source.Length < 2)
                        {
                            return ZstdFrameStatus.Truncated;
                        }

                        headerSize = 2;
                        regeneratedSize = BinaryPrimitives.ReadUInt16LittleEndian(source) >> 4;
                        break;
                    case 3:
                        if (source.Length < 3)
                        {
                            return ZstdFrameStatus.Truncated;
                        }

                        headerSize = 3;
                        regeneratedSize = ReadUInt24(source) >> 4;
                        break;
                    default:
                        return ZstdFrameStatus.Corrupt;
                }

                if (regeneratedSize > blockMaximumSize)
                {
                    return ZstdFrameStatus.Corrupt;
                }

                int payloadSize = literalType == 0 ? regeneratedSize : 1;
                if (source.Length - headerSize < payloadSize)
                {
                    return ZstdFrameStatus.Truncated;
                }

                literals = new byte[regeneratedSize];
                if (literalType == 0)
                {
                    source.Slice(headerSize, regeneratedSize).CopyTo(literals);
                }
                else
                {
                    literals.AsSpan().Fill(source[headerSize]);
                }

                bytesConsumed = checked(headerSize + payloadSize);
                return ZstdFrameStatus.Success;
            }

            int compressedHeaderSize;
            int literalSize;
            int literalCompressedSize;
            bool singleStream;
            switch (sizeFormat)
            {
                case 0:
                case 1:
                    if (source.Length < 3)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    uint shortHeader = (uint)ReadUInt24(source);
                    compressedHeaderSize = 3;
                    literalSize = (int)((shortHeader >> 4) & 0x03ff);
                    literalCompressedSize = (int)((shortHeader >> 14) & 0x03ff);
                    singleStream = sizeFormat == 0;
                    break;
                case 2:
                    if (source.Length < 4)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    uint mediumHeader = BinaryPrimitives.ReadUInt32LittleEndian(source);
                    compressedHeaderSize = 4;
                    literalSize = (int)((mediumHeader >> 4) & 0x3fff);
                    literalCompressedSize = (int)(mediumHeader >> 18);
                    singleStream = false;
                    break;
                case 3:
                    if (source.Length < 5)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    uint longHeader = BinaryPrimitives.ReadUInt32LittleEndian(source);
                    compressedHeaderSize = 5;
                    literalSize = (int)((longHeader >> 4) & 0x3ffff);
                    literalCompressedSize = checked((int)(longHeader >> 22) + (source[4] << 10));
                    singleStream = false;
                    break;
                default:
                    return ZstdFrameStatus.Corrupt;
            }

            if (literalSize > blockMaximumSize || literalCompressedSize == 0
                || source.Length - compressedHeaderSize < literalCompressedSize
                || (!singleStream && literalSize < 6))
            {
                return source.Length - compressedHeaderSize < literalCompressedSize
                    ? ZstdFrameStatus.Truncated
                    : ZstdFrameStatus.Corrupt;
            }

            ReadOnlySpan<byte> compressedLiterals = source.Slice(compressedHeaderSize, literalCompressedSize);
            ZstdHuffmanTable? huffmanTable = state.HuffmanTable;
            int huffmanHeaderSize = 0;
            if (literalType == 2)
            {
                ZstdFrameStatus tableStatus = ZstdHuffmanTable.Read(
                    compressedLiterals,
                    out huffmanTable,
                    out huffmanHeaderSize);
                if (tableStatus != ZstdFrameStatus.Success || huffmanTable is null)
                {
                    return tableStatus;
                }
            }
            else if (huffmanTable is null)
            {
                return ZstdFrameStatus.Corrupt;
            }

            if (huffmanHeaderSize >= compressedLiterals.Length)
            {
                return ZstdFrameStatus.Corrupt;
            }

            ReadOnlySpan<byte> streams = compressedLiterals.Slice(huffmanHeaderSize);
            literals = new byte[literalSize];
            ZstdFrameStatus decodeStatus;
            if (singleStream)
            {
                decodeStatus = huffmanTable.DecodeStream(streams, literals);
            }
            else
            {
                decodeStatus = DecodeFourHuffmanStreams(huffmanTable, streams, literals);
            }

            if (decodeStatus != ZstdFrameStatus.Success)
            {
                literals = Array.Empty<byte>();
                return decodeStatus;
            }

            state.HuffmanTable = huffmanTable;
            bytesConsumed = checked(compressedHeaderSize + literalCompressedSize);
            return ZstdFrameStatus.Success;
        }

        private static ZstdFrameStatus DecodeFourHuffmanStreams(
            ZstdHuffmanTable table,
            ReadOnlySpan<byte> source,
            Span<byte> destination)
        {
            if (source.Length < 10 || destination.Length < 6)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int length1 = BinaryPrimitives.ReadUInt16LittleEndian(source);
            int length2 = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(2));
            int length3 = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4));
            int streamBytes = source.Length - 6;
            if (length1 <= 0 || length2 <= 0 || length3 <= 0
                || length1 > streamBytes
                || length2 > streamBytes - length1
                || length3 > streamBytes - length1 - length2)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int length4 = streamBytes - length1 - length2 - length3;
            if (length4 <= 0)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int segmentSize = checked((destination.Length + 3) / 4);
            int segment1End = segmentSize;
            int segment2End = checked(segment1End + segmentSize);
            int segment3End = checked(segment2End + segmentSize);
            if (segment3End > destination.Length)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int streamOffset = 6;
            ZstdFrameStatus status = table.DecodeStream(
                source.Slice(streamOffset, length1),
                destination.Slice(0, segmentSize));
            if (status != ZstdFrameStatus.Success)
            {
                return status;
            }

            streamOffset += length1;
            status = table.DecodeStream(
                source.Slice(streamOffset, length2),
                destination.Slice(segment1End, segmentSize));
            if (status != ZstdFrameStatus.Success)
            {
                return status;
            }

            streamOffset += length2;
            status = table.DecodeStream(
                source.Slice(streamOffset, length3),
                destination.Slice(segment2End, segmentSize));
            if (status != ZstdFrameStatus.Success)
            {
                return status;
            }

            streamOffset += length3;
            return table.DecodeStream(
                source.Slice(streamOffset, length4),
                destination.Slice(segment3End));
        }

        private static ZstdFrameStatus ReadSequenceTable(
            ReadOnlySpan<byte> source,
            ref int offset,
            int mode,
            int maximumSymbol,
            int maximumTableLog,
            ZstdFseTable defaultTable,
            ZstdFseTable? previousTable,
            bool hasPreviousTables,
            out ZstdFseTable? table)
        {
            table = null;
            switch (mode)
            {
                case 0:
                    table = defaultTable;
                    return ZstdFrameStatus.Success;
                case 1:
                    if (offset >= source.Length)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    ZstdFrameStatus rleStatus = ZstdFseTable.CreateRle(source[offset], maximumSymbol, out table);
                    offset++;
                    return rleStatus;
                case 2:
                    if (offset >= source.Length)
                    {
                        return ZstdFrameStatus.Truncated;
                    }

                    ZstdFrameStatus compressedStatus = ZstdFseTable.Read(
                        source.Slice(offset),
                        maximumSymbol,
                        maximumTableLog,
                        out table,
                        out int bytesConsumed);
                    if (compressedStatus == ZstdFrameStatus.Success)
                    {
                        offset = checked(offset + bytesConsumed);
                    }

                    return compressedStatus;
                case 3:
                    if (!hasPreviousTables || previousTable is null)
                    {
                        return ZstdFrameStatus.Corrupt;
                    }

                    table = previousTable;
                    return ZstdFrameStatus.Success;
                default:
                    return ZstdFrameStatus.Corrupt;
            }
        }

        private static ZstdFrameStatus ExecuteSequence(
            ReadOnlySpan<byte> literals,
            ref int literalOffset,
            ulong literalLength,
            ulong matchLength,
            ulong matchOffset,
            Span<byte> output,
            ref int outputOffset,
            int blockOutputOffset,
            int blockMaximumSize,
            ulong windowSize,
            bool hasExpectedFrameSize,
            ulong expectedFrameSize)
        {
            if (literalLength > (ulong)(literals.Length - literalOffset)
                || literalLength > int.MaxValue
                || matchLength > int.MaxValue)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int literalCount = (int)literalLength;
            int matchCount = (int)matchLength;
            int blockBytes = outputOffset - blockOutputOffset;
            if (literalCount > blockMaximumSize - blockBytes
                || matchCount > blockMaximumSize - blockBytes - literalCount)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int sequenceSize = checked(literalCount + matchCount);
            if (sequenceSize > ZstdFrameLimits.MaximumOutputSize - outputOffset)
            {
                return ZstdFrameStatus.ContentSizeTooLarge;
            }

            if (hasExpectedFrameSize && (ulong)outputOffset + (ulong)sequenceSize > expectedFrameSize)
            {
                return ZstdFrameStatus.ContentSizeMismatch;
            }

            if (sequenceSize > output.Length - outputOffset)
            {
                return ZstdFrameStatus.DestinationTooSmall;
            }

            literals.Slice(literalOffset, literalCount).CopyTo(output.Slice(outputOffset, literalCount));
            literalOffset += literalCount;
            outputOffset += literalCount;

            ulong availableHistory = Math.Min((ulong)outputOffset, windowSize);
            if (matchOffset == 0 || matchOffset > availableHistory || matchOffset > int.MaxValue)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int offset = (int)matchOffset;
            for (int index = 0; index < matchCount; index++)
            {
                output[outputOffset + index] = output[outputOffset + index - offset];
            }

            outputOffset += matchCount;
            return ZstdFrameStatus.Success;
        }

        private static ZstdFrameStatus CopyRemainingLiterals(
            ReadOnlySpan<byte> literals,
            int literalOffset,
            Span<byte> output,
            ref int outputOffset,
            int blockOutputOffset,
            int blockMaximumSize,
            bool hasExpectedFrameSize,
            ulong expectedFrameSize)
        {
            if (literalOffset < 0 || literalOffset > literals.Length)
            {
                return ZstdFrameStatus.Corrupt;
            }

            int count = literals.Length - literalOffset;
            int blockBytes = outputOffset - blockOutputOffset;
            if (count > blockMaximumSize - blockBytes)
            {
                return ZstdFrameStatus.Corrupt;
            }

            if (count > ZstdFrameLimits.MaximumOutputSize - outputOffset)
            {
                return ZstdFrameStatus.ContentSizeTooLarge;
            }

            if (hasExpectedFrameSize && (ulong)outputOffset + (ulong)count > expectedFrameSize)
            {
                return ZstdFrameStatus.ContentSizeMismatch;
            }

            if (count > output.Length - outputOffset)
            {
                return ZstdFrameStatus.DestinationTooSmall;
            }

            literals.Slice(literalOffset).CopyTo(output.Slice(outputOffset, count));
            outputOffset += count;
            return ZstdFrameStatus.Success;
        }

        private static ZstdFrameStatus CopyLastLiterals(
            ReadOnlySpan<byte> literals,
            Span<byte> output,
            int blockOutputOffset,
            int blockMaximumSize,
            bool hasExpectedFrameSize,
            ulong expectedFrameSize,
            out int bytesWritten)
        {
            int outputOffset = blockOutputOffset;
            ZstdFrameStatus status = CopyRemainingLiterals(
                literals,
                literalOffset: 0,
                output,
                ref outputOffset,
                blockOutputOffset,
                blockMaximumSize,
                hasExpectedFrameSize,
                expectedFrameSize);
            bytesWritten = status == ZstdFrameStatus.Success ? literals.Length : 0;
            return status;
        }

        private static ZstdFseTable CreateDefaultTable(short[] normalizedCounts, int tableLog)
        {
            ZstdFrameStatus status = ZstdFseTable.Build(
                normalizedCounts,
                normalizedCounts.Length - 1,
                tableLog,
                out ZstdFseTable? table);
            if (status != ZstdFrameStatus.Success || table is null)
            {
                throw new InvalidOperationException("Invalid built-in Zstandard FSE distribution.");
            }

            return table;
        }

        private static int ReadUInt24(ReadOnlySpan<byte> source)
        {
            return source[0] | (source[1] << 8) | (source[2] << 16);
        }
    }
}
