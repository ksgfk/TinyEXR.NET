using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using TinyEXR.V3.Format;

namespace TinyEXR.V3
{
    internal sealed class ReaderParseException : Exception
    {
        public ReaderParseException(ExrResult result, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Result = result;
        }

        public ExrResult Result { get; }
    }

    internal readonly struct ReaderParserRequest
    {
        public ReaderParserRequest(long offset, byte[] buffer, int bufferOffset, int length)
        {
            Offset = offset;
            Buffer = buffer;
            BufferOffset = bufferOffset;
            Length = length;
        }

        public long Offset { get; }

        public byte[] Buffer { get; }

        public int BufferOffset { get; }

        public int Length { get; }

        public Span<byte> Span => Buffer.AsSpan(BufferOffset, Length);

        public Memory<byte> Memory => Buffer.AsMemory(BufferOffset, Length);
    }

    /// <summary>
    /// Source-independent incremental EXR prefix/header/index parser. The machine advances only
    /// after an exact request succeeds, so cancellation, I/O failure, and WouldBlock are retryable.
    /// </summary>
    internal sealed class ReaderParser
    {
        private readonly ReaderLimits _limits;
        private readonly byte[] _prefix = new byte[8];
        private readonly byte[] _singleByte = new byte[1];
        private readonly byte[] _sizeBytes = new byte[4];
        private readonly byte[] _offsetBytes = new byte[8];
        private readonly byte[] _stringBytes = new byte[255];
        private readonly List<HeaderAttribute> _attributes = new List<HeaderAttribute>();
        private readonly HashSet<string> _attributeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ReaderPartDraft> _parts = new List<ReaderPartDraft>();

        private ParserPhase _phase = ParserPhase.Prefix;
        private long _position;
        private ParsedFileFlags _flags;
        private int _nameByteLimit;
        private int _stringLength;
        private string? _attributeName;
        private string? _attributeType;
        private byte[]? _attributePayload;
        private int _attributePayloadOffset;
        private int _totalAttributeCount;
        private long _totalAttributeByteCount;
        private int _offsetPartIndex;
        private int _offsetEntryIndex;
        private uint _rawVersionField;

        public ReaderParser(ReaderLimits limits)
        {
            _limits = limits;
        }

        public ReaderState State
        {
            get
            {
                switch (_phase)
                {
                    case ParserPhase.Prefix:
                        return ReaderState.ReadingPrefix;
                    case ParserPhase.AttributeName:
                    case ParserPhase.AttributeType:
                    case ParserPhase.AttributeSize:
                    case ParserPhase.AttributePayload:
                        return ReaderState.ReadingHeaders;
                    case ParserPhase.Offset:
                        return ReaderState.ReadingOffsetTables;
                    case ParserPhase.Complete:
                        return ReaderState.Ready;
                    default:
                        throw new InvalidOperationException("Unknown reader parser phase.");
                }
            }
        }

        public bool IsComplete => _phase == ParserPhase.Complete;

        public ReaderFileData? FileData { get; private set; }

        public ReaderParserRequest GetNextRequest()
        {
            switch (_phase)
            {
                case ParserPhase.Prefix:
                    return new ReaderParserRequest(_position, _prefix, 0, _prefix.Length);
                case ParserPhase.AttributeName:
                case ParserPhase.AttributeType:
                    return new ReaderParserRequest(_position, _singleByte, 0, 1);
                case ParserPhase.AttributeSize:
                    return new ReaderParserRequest(_position, _sizeBytes, 0, _sizeBytes.Length);
                case ParserPhase.AttributePayload:
                    int remaining = _attributePayload!.Length - _attributePayloadOffset;
                    int length = Math.Min(remaining, _limits.MaximumReadRequestByteCount);
                    return new ReaderParserRequest(
                        _position,
                        _attributePayload,
                        _attributePayloadOffset,
                        length);
                case ParserPhase.Offset:
                    return new ReaderParserRequest(_position, _offsetBytes, 0, _offsetBytes.Length);
                case ParserPhase.Complete:
                    throw new InvalidOperationException("The parser is already complete.");
                default:
                    throw new InvalidOperationException("Unknown reader parser phase.");
            }
        }

        public void AcceptNextRequest(long? knownLength)
        {
            ParserPhase acceptedPhase = _phase;
            ReaderParserRequest request = GetNextRequest();
            _position = checked(_position + request.Length);
            if (acceptedPhase != ParserPhase.Prefix && acceptedPhase != ParserPhase.Offset)
            {
                EnforceLimit(
                    nameof(ReaderLimits.MaximumHeaderByteCount),
                    checked(_position - _prefix.Length),
                    _limits.MaximumHeaderByteCount);
            }

            switch (acceptedPhase)
            {
                case ParserPhase.Prefix:
                    AcceptPrefix();
                    break;
                case ParserPhase.AttributeName:
                    AcceptStringByte(isAttributeName: true, knownLength);
                    break;
                case ParserPhase.AttributeType:
                    AcceptStringByte(isAttributeName: false, knownLength);
                    break;
                case ParserPhase.AttributeSize:
                    AcceptAttributeSize(knownLength);
                    break;
                case ParserPhase.AttributePayload:
                    AcceptAttributePayload(request.Length);
                    break;
                case ParserPhase.Offset:
                    AcceptOffset(knownLength);
                    break;
                default:
                    throw new InvalidOperationException("The parser cannot accept this phase.");
            }
        }

        private void AcceptPrefix()
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(_prefix) != ExrFormatParser.Magic)
            {
                throw Failure(ExrResult.InvalidFile, "The source does not begin with the OpenEXR magic number.");
            }

            uint versionField = BinaryPrimitives.ReadUInt32LittleEndian(_prefix.AsSpan(4));
            _rawVersionField = versionField;
            ExrResult result = ExrFormatParser.InterpretVersionField(
                versionField,
                out _flags,
                out _nameByteLimit);
            if (result != ExrResult.Success)
            {
                throw Failure(result, "The OpenEXR version field is not supported or is inconsistent.");
            }

            _phase = ParserPhase.AttributeName;
        }

        private void AcceptStringByte(bool isAttributeName, long? knownLength)
        {
            byte value = _singleByte[0];
            if (value != 0)
            {
                if (_stringLength >= _nameByteLimit)
                {
                    throw Failure(ExrResult.Corrupt, "An EXR name exceeds the version-field byte limit.");
                }

                _stringBytes[_stringLength++] = value;
                return;
            }

            if (isAttributeName && _stringLength == 0)
            {
                AcceptHeaderTerminator(knownLength);
                return;
            }

            if (_stringLength == 0)
            {
                throw Failure(ExrResult.Corrupt, "An EXR attribute type name is empty.");
            }

            string decoded;
            try
            {
                decoded = ModelValidation.StrictUtf8.GetString(
                    _stringBytes.AsSpan(0, _stringLength).ToArray());
            }
            catch (System.Text.DecoderFallbackException exception)
            {
                throw Failure(ExrResult.Corrupt, "An EXR name is not valid UTF-8.", exception);
            }
            finally
            {
                _stringLength = 0;
            }

            if (isAttributeName)
            {
                if (_parts.Count >= _limits.MaximumParts && _attributes.Count == 0)
                {
                    throw Limit(
                        nameof(ReaderLimits.MaximumParts),
                        checked((long)_parts.Count + 1L),
                        _limits.MaximumParts);
                }

                if (_attributes.Count >= _limits.MaximumAttributesPerPart)
                {
                    throw Limit(
                        nameof(ReaderLimits.MaximumAttributesPerPart),
                        checked((long)_attributes.Count + 1L),
                        _limits.MaximumAttributesPerPart);
                }

                if (_totalAttributeCount >= _limits.MaximumTotalAttributes)
                {
                    throw Limit(
                        nameof(ReaderLimits.MaximumTotalAttributes),
                        checked((long)_totalAttributeCount + 1L),
                        _limits.MaximumTotalAttributes);
                }

                if (!_attributeNames.Add(decoded))
                {
                    throw Failure(ExrResult.Corrupt, $"Duplicate EXR attribute '{decoded}'.");
                }

                _attributeName = decoded;
                _phase = ParserPhase.AttributeType;
            }
            else
            {
                _attributeType = decoded;
                _phase = ParserPhase.AttributeSize;
            }
        }

        private void AcceptAttributeSize(long? knownLength)
        {
            int byteCount = BinaryPrimitives.ReadInt32LittleEndian(_sizeBytes);
            if (byteCount < 0)
            {
                throw Failure(ExrResult.Corrupt, "An EXR attribute has a negative byte count.");
            }

            EnforceLimit(
                nameof(ReaderLimits.MaximumAttributeByteCount),
                byteCount,
                _limits.MaximumAttributeByteCount);
            long aggregate = checked(_totalAttributeByteCount + byteCount);
            EnforceLimit(
                nameof(ReaderLimits.MaximumTotalAttributeByteCount),
                aggregate,
                _limits.MaximumTotalAttributeByteCount);

            long end = checked(_position + byteCount);
            EnforceLimit(
                nameof(ReaderLimits.MaximumHeaderByteCount),
                checked(end - _prefix.Length),
                _limits.MaximumHeaderByteCount);
            if (knownLength.HasValue && end > knownLength.Value)
            {
                throw Failure(
                    ExrResult.Corrupt,
                    "An EXR attribute payload extends past the source length.");
            }

            _attributePayload = byteCount == 0 ? Array.Empty<byte>() : new byte[byteCount];
            _attributePayloadOffset = 0;
            if (byteCount == 0)
            {
                CompleteAttribute();
            }
            else
            {
                _phase = ParserPhase.AttributePayload;
            }
        }

        private void AcceptAttributePayload(int acceptedByteCount)
        {
            _attributePayloadOffset = checked(_attributePayloadOffset + acceptedByteCount);
            if (_attributePayloadOffset == _attributePayload!.Length)
            {
                CompleteAttribute();
            }
        }

        private void CompleteAttribute()
        {
            byte[] payload = _attributePayload!;
            _attributes.Add(HeaderAttribute.Adopt(_attributeName!, _attributeType!, payload));
            _totalAttributeCount = checked(_totalAttributeCount + 1);
            _totalAttributeByteCount = checked(_totalAttributeByteCount + payload.Length);
            _attributeName = null;
            _attributeType = null;
            _attributePayload = null;
            _attributePayloadOffset = 0;
            _phase = ParserPhase.AttributeName;
        }

        private void AcceptHeaderTerminator(long? knownLength)
        {
            if (_attributes.Count == 0)
            {
                if (_flags.Multipart && _parts.Count > 0)
                {
                    FinalizeHeaders(knownLength);
                    return;
                }

                throw Failure(ExrResult.Corrupt, "An EXR header does not contain attributes.");
            }

            EnforceChannelLimit();

            ExrResult result = ExrFormatParser.InterpretHeaderAttributes(
                _parts.Count,
                _attributes,
                _flags,
                out Header? header,
                out int blockCount);
            if (result != ExrResult.Success || header == null)
            {
                throw Failure(result, "The EXR header attributes are invalid or unsupported.");
            }

            EnforceLimit(
                nameof(ReaderLimits.MaximumChannelsPerPart),
                header.Channels.Count,
                _limits.MaximumChannelsPerPart);
            EnforceLimit(
                nameof(ReaderLimits.MaximumDimension),
                Math.Max(header.DataWindow.Width, header.DataWindow.Height),
                _limits.MaximumDimension);
            EnforceLimit(
                nameof(ReaderLimits.MaximumBlocksPerPart),
                blockCount,
                _limits.MaximumBlocksPerPart);

            ReaderPartLayout layout;
            try
            {
                layout = ReaderPartLayout.Create(header, _flags.Multipart, blockCount);
            }
            catch (OverflowException exception)
            {
                throw Failure(ExrResult.Corrupt, "The EXR part geometry overflows its index.", exception);
            }
            catch (ArgumentException exception)
            {
                throw Failure(ExrResult.Corrupt, "The EXR part geometry is invalid.", exception);
            }
            catch (InvalidOperationException exception)
            {
                throw Failure(ExrResult.Corrupt, "The EXR part geometry is inconsistent.", exception);
            }

            _parts.Add(new ReaderPartDraft(
                header,
                blockCount,
                layout,
                _position,
                _attributeNames.Contains("name"),
                _attributeNames.Contains("type"),
                _attributes.ToArray()));
            _attributes.Clear();
            _attributeNames.Clear();

            if (_flags.Multipart)
            {
                _phase = ParserPhase.AttributeName;
            }
            else
            {
                FinalizeHeaders(knownLength);
            }
        }

        private void EnforceChannelLimit()
        {
            HeaderAttribute? channels = null;
            for (int i = 0; i < _attributes.Count; i++)
            {
                if (string.Equals(_attributes[i].Name, "channels", StringComparison.Ordinal))
                {
                    channels = _attributes[i];
                    break;
                }
            }

            if (channels == null)
            {
                return;
            }

            ReadOnlySpan<byte> data = channels.Data;
            int offset = 0;
            int count = 0;
            while (offset < data.Length && data[offset] != 0)
            {
                int nameStart = offset;
                while (offset < data.Length && data[offset] != 0)
                {
                    offset++;
                }

                if (offset == data.Length || offset - nameStart > _nameByteLimit)
                {
                    throw Failure(ExrResult.Corrupt, "The EXR channel list is truncated or has an overlong name.");
                }

                offset++;
                const int channelDescriptorByteCount = 16;
                if (data.Length - offset < channelDescriptorByteCount)
                {
                    throw Failure(ExrResult.Corrupt, "The EXR channel list is truncated.");
                }

                offset += channelDescriptorByteCount;
                count = checked(count + 1);
                if (count > _limits.MaximumChannelsPerPart)
                {
                    throw Limit(
                        nameof(ReaderLimits.MaximumChannelsPerPart),
                        count,
                        _limits.MaximumChannelsPerPart);
                }
            }
        }

        private void FinalizeHeaders(long? knownLength)
        {
            if (_parts.Count == 0)
            {
                throw Failure(ExrResult.Corrupt, "The EXR file has an invalid number of parts.");
            }

            Header[] headers = new Header[_parts.Count];
            long totalBlocks = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                headers[i] = _parts[i].Header;
                totalBlocks = checked(totalBlocks + _parts[i].BlockCount);
            }

            ExrResult validation = ExrFormatParser.ValidateHeaders(headers, _flags);
            if (validation != ExrResult.Success)
            {
                throw Failure(validation, "The EXR version flags and part headers are inconsistent.");
            }

            EnforceLimit(
                nameof(ReaderLimits.MaximumTotalBlocks),
                totalBlocks,
                _limits.MaximumTotalBlocks);
            long offsetTableBytes = checked(totalBlocks * sizeof(ulong));
            EnforceLimit(
                nameof(ReaderLimits.MaximumOffsetTableByteCount),
                offsetTableBytes,
                _limits.MaximumOffsetTableByteCount);

            long offsetTableEnd = checked(_position + offsetTableBytes);
            if (knownLength.HasValue && offsetTableEnd > knownLength.Value)
            {
                throw Failure(
                    ExrResult.Corrupt,
                    "The EXR offset tables extend past the source length.");
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                _parts[i].Offsets = new ulong[_parts[i].BlockCount];
            }

            _offsetPartIndex = 0;
            _offsetEntryIndex = 0;
            _phase = ParserPhase.Offset;
        }

        private void AcceptOffset(long? knownLength)
        {
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(_offsetBytes);
            if (offset > long.MaxValue)
            {
                throw Failure(ExrResult.Unsupported, "An EXR chunk offset exceeds the managed source range.");
            }

            ReaderPartDraft part = _parts[_offsetPartIndex];
            part.Offsets![_offsetEntryIndex] = offset;
            _offsetEntryIndex++;
            if (_offsetEntryIndex == part.BlockCount)
            {
                _offsetPartIndex++;
                _offsetEntryIndex = 0;
            }

            if (_offsetPartIndex == _parts.Count)
            {
                CompleteOffsets(knownLength);
            }
        }

        private void CompleteOffsets(long? knownLength)
        {
            long pixelDataStart = _position;
            if (knownLength.HasValue && knownLength.Value < pixelDataStart)
            {
                throw Failure(ExrResult.Corrupt, "The EXR offset tables extend past the source length.");
            }

            ReaderPartData[] completed = new ReaderPartData[_parts.Count];
            for (int partIndex = 0; partIndex < _parts.Count; partIndex++)
            {
                ReaderPartDraft draft = _parts[partIndex];
                ulong[] offsets = draft.Offsets!;
                int headerByteCount = ExrFormatParser.ChunkHeaderByteCount(draft.Header, _flags.Multipart);
                for (int blockIndex = 0; blockIndex < offsets.Length; blockIndex++)
                {
                    ulong offset = offsets[blockIndex];
                    if (offset == 0)
                    {
                        continue;
                    }

                    if (offset < (ulong)pixelDataStart)
                    {
                        throw Failure(ExrResult.Corrupt, "An EXR chunk offset points into the header or index.");
                    }

                    if (knownLength.HasValue)
                    {
                        ulong length = checked((ulong)knownLength.Value);
                        if (offset > length || (ulong)headerByteCount > length - offset)
                        {
                            throw Failure(ExrResult.Corrupt, "An EXR chunk header is outside the source length.");
                        }
                    }
                }

                completed[partIndex] = new ReaderPartData(
                    partIndex,
                    draft.Header,
                    offsets,
                    draft.Layout,
                    draft.HeaderEnd,
                    draft.HasNameAttribute,
                    draft.HasTypeAttribute,
                    draft.RawAttributes);
            }

            FileData = new ReaderFileData(
                _rawVersionField,
                _flags.Multipart,
                pixelDataStart,
                completed);
            _phase = ParserPhase.Complete;
        }

        private static void EnforceLimit(string name, long actual, long maximum)
        {
            if (actual > maximum)
            {
                throw Limit(name, actual, maximum);
            }
        }

        private static ReaderParseException Limit(string name, long actual, long maximum)
        {
            ReaderLimitExceededException error = new ReaderLimitExceededException(name, actual, maximum);
            return new ReaderParseException(ExrResult.Unsupported, error.Message, error);
        }

        private static ReaderParseException Failure(
            ExrResult result,
            string message,
            Exception? innerException = null)
        {
            return new ReaderParseException(result, message, innerException);
        }

        private enum ParserPhase
        {
            Prefix,
            AttributeName,
            AttributeType,
            AttributeSize,
            AttributePayload,
            Offset,
            Complete,
        }

        private sealed class ReaderPartDraft
        {
            public ReaderPartDraft(
                Header header,
                int blockCount,
                ReaderPartLayout layout,
                long headerEnd,
                bool hasNameAttribute,
                bool hasTypeAttribute,
                HeaderAttribute[] rawAttributes)
            {
                Header = header;
                BlockCount = blockCount;
                Layout = layout;
                HeaderEnd = headerEnd;
                HasNameAttribute = hasNameAttribute;
                HasTypeAttribute = hasTypeAttribute;
                RawAttributes = rawAttributes;
            }

            public Header Header { get; }

            public int BlockCount { get; }

            public ReaderPartLayout Layout { get; }

            public long HeaderEnd { get; }

            public bool HasNameAttribute { get; }

            public bool HasTypeAttribute { get; }

            public HeaderAttribute[] RawAttributes { get; }

            public ulong[]? Offsets { get; set; }
        }
    }
}
