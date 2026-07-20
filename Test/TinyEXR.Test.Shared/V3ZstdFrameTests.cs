using System.Buffers.Binary;
using System.Text;
using TinyEXR.V3.Codecs;

namespace TinyEXR.Test;

[TestClass]
public sealed class V3ZstdFrameTests
{
    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD raw frame matches specification golden")]
    public void Case_V3ZstdFrame_RawEncoderMatchesSpecificationGolden()
    {
        byte[] source = Encoding.ASCII.GetBytes("hello");
        byte[] encoded = Encode(source, includeChecksum: false);
        byte[] expected =
        {
            0x28, 0xb5, 0x2f, 0xfd, // Zstandard magic
            0x20,                   // single segment, one-byte content size
            0x05,                   // frame content size
            0x29, 0x00, 0x00,       // final raw block, five bytes
            0x68, 0x65, 0x6c, 0x6c, 0x6f,
        };

        CollectionAssert.AreEqual(expected, encoded);
        AssertDecoded(encoded, source);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD RLE frame and xxHash checksum match goldens")]
    public void Case_V3ZstdFrame_RleEncoderAndChecksumMatchGoldens()
    {
        byte[] source = Enumerable.Repeat((byte)0xa5, 512).ToArray();
        byte[] encoded = Encode(source, includeChecksum: true);
        byte[] expected =
        {
            0x28, 0xb5, 0x2f, 0xfd,
            0x64,             // two-byte content size, single segment, checksum
            0x00, 0x01,       // 512 - 256
            0x03, 0x10, 0x00, // final RLE block, regenerated size 512
            0xa5,
            0x8c, 0xe4, 0x25, 0x08, // low 32 bits of xxh64(source, seed 0)
        };

        CollectionAssert.AreEqual(expected, encoded);
        Assert.AreEqual(0xef46db3751d8e999UL, XxHash64.Compute(ReadOnlySpan<byte>.Empty));
        Assert.AreEqual(0x8cb841db40e6ae83UL, XxHash64.Compute(Encoding.ASCII.GetBytes("123456789")));
        AssertDecoded(encoded, source);

        encoded[^1] ^= 0x80;
        byte[] destination = new byte[source.Length];
        Assert.AreEqual(
            ZstdFrameStatus.ChecksumMismatch,
            ZstdFrameDecoder.Decode(encoded, destination, out _, out int bytesWritten, out _));
        Assert.AreEqual(source.Length, bytesWritten);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD empty and mixed multi-block frames round trip")]
    public void Case_V3ZstdFrame_EmptyAndMultiBlockRoundTrip()
    {
        byte[] emptyEncoded = Encode(Array.Empty<byte>(), includeChecksum: false);
        CollectionAssert.AreEqual(
            new byte[] { 0x28, 0xb5, 0x2f, 0xfd, 0x20, 0x00, 0x01, 0x00, 0x00 },
            emptyEncoded);
        AssertDecoded(emptyEncoded, Array.Empty<byte>());

        byte[] source = new byte[(2 * ZstdFrameLimits.MaximumBlockSize) + 17];
        Array.Fill(source, (byte)0x11, 0, ZstdFrameLimits.MaximumBlockSize);
        for (int i = ZstdFrameLimits.MaximumBlockSize; i < 2 * ZstdFrameLimits.MaximumBlockSize; i++)
        {
            source[i] = (byte)(i % 251);
        }

        Array.Fill(source, (byte)0xcc, 2 * ZstdFrameLimits.MaximumBlockSize, 17);
        byte[] encoded = Encode(source, includeChecksum: true);

        Assert.IsTrue(encoded.Length < source.Length, "The RLE blocks should make this frame smaller than its source.");
        AssertDecoded(encoded, source);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD parses every frame content size representation")]
    public void Case_V3ZstdFrame_ParsesContentSizeVariants()
    {
        AssertHeaderContentSize(Header(0x20, new byte[] { 42 }), 42, expectedWindowSize: 42, expectedHeaderSize: 6);
        AssertHeaderContentSize(Header(0x60, new byte[] { 44, 0 }), 300, expectedWindowSize: 300, expectedHeaderSize: 7);

        byte[] fourByteSize = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(fourByteSize, 100_000);
        AssertHeaderContentSize(Header(0xa0, fourByteSize), 100_000, expectedWindowSize: 100_000, expectedHeaderSize: 9);

        byte[] eightByteSize = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(eightByteSize, 100_000);
        AssertHeaderContentSize(Header(0xe0, eightByteSize), 100_000, expectedWindowSize: 100_000, expectedHeaderSize: 13);

        byte[] unknownSizeHeader = Header(0x00, new byte[] { 0x00 });
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameHeaderParser.Parse(unknownSizeHeader, out ZstdFrameHeader unknownSize, out int bytesConsumed));
        Assert.IsFalse(unknownSize.HasFrameContentSize);
        Assert.AreEqual(1024UL, unknownSize.WindowSize);
        Assert.AreEqual(6, bytesConsumed);

        byte[] customWindowHeader = Header(0x00, new byte[] { 0x1d });
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameHeaderParser.Parse(customWindowHeader, out ZstdFrameHeader customWindow, out _));
        Assert.AreEqual(13_312UL, customWindow.WindowSize);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD preserves dictionary metadata and rejects dictionaries explicitly")]
    public void Case_V3ZstdFrame_DictionaryMetadataAndRejection()
    {
        byte[] dictionaryBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(dictionaryBytes, 0x12345678);
        byte[] frame = Frame(
            descriptor: 0x23,
            headerFields: dictionaryBytes.Concat(new byte[] { 0 }).ToArray(),
            blockAndTrailer: new byte[] { 0x01, 0x00, 0x00 });

        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameHeaderParser.Parse(frame, out ZstdFrameHeader header, out int headerBytes));
        Assert.IsTrue(header.HasDictionaryId);
        Assert.AreEqual(0x12345678U, header.DictionaryId);
        Assert.AreEqual(10, headerBytes);
        Assert.AreEqual(
            ZstdFrameStatus.DictionaryNotSupported,
            ZstdFrameDecoder.Decode(frame, Span<byte>.Empty, out _, out _, out _));

        AssertDictionaryField(0x21, new byte[] { 0x7f, 0x00 }, 0x7fU);
        AssertDictionaryField(0x22, new byte[] { 0x34, 0x12, 0x00 }, 0x1234U);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD reports and consumes one skippable frame")]
    public void Case_V3ZstdFrame_SkippableFrameBehavior()
    {
        byte[] frame = new byte[11];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, 0x184D2A5A);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), 3);
        frame[8] = 1;
        frame[9] = 2;
        frame[10] = 3;

        Assert.AreEqual(
            ZstdFrameStatus.Skipped,
            ZstdFrameHeaderParser.Parse(frame, out ZstdFrameHeader header, out int bytesConsumed));
        Assert.IsTrue(header.IsSkippable);
        Assert.AreEqual(3U, header.SkippablePayloadSize);
        Assert.AreEqual(frame.Length, bytesConsumed);
        Assert.AreEqual(
            ZstdFrameStatus.Skipped,
            ZstdFrameDecoder.Decode(frame, Span<byte>.Empty, out bytesConsumed, out int bytesWritten, out _));
        Assert.AreEqual(frame.Length, bytesConsumed);
        Assert.AreEqual(0, bytesWritten);

        Assert.AreEqual(
            ZstdFrameStatus.Truncated,
            ZstdFrameHeaderParser.Parse(frame.AsSpan(0, frame.Length - 1), out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD rejects reserved fields and oversized resource requests")]
    public void Case_V3ZstdFrame_RejectsReservedAndResourceLimits()
    {
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameHeaderParser.Parse(Header(0x28, new byte[] { 0 }), out _, out _));

        // The unused bit is intentionally ignored by compliant decoders.
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameHeaderParser.Parse(Header(0x30, new byte[] { 0 }), out _, out _));

        Assert.AreEqual(
            ZstdFrameStatus.WindowTooLarge,
            ZstdFrameHeaderParser.Parse(Header(0x00, new byte[] { 0xff }), out _, out _));

        byte[] oversizedContent = new byte[1 + 8];
        oversizedContent[0] = 0x00; // one KiB window
        BinaryPrimitives.WriteUInt64LittleEndian(
            oversizedContent.AsSpan(1),
            (ulong)ZstdFrameLimits.MaximumOutputSize + 1UL);
        Assert.AreEqual(
            ZstdFrameStatus.ContentSizeTooLarge,
            ZstdFrameHeaderParser.Parse(Header(0xc0, oversizedContent), out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD detects every truncation of a valid frame")]
    public void Case_V3ZstdFrame_TruncationIsNeverAccepted()
    {
        byte[] encoded = Encode(Encoding.ASCII.GetBytes("checksum protected"), includeChecksum: true);
        byte[] destination = new byte[128];
        for (int length = 0; length < encoded.Length; length++)
        {
            ZstdFrameStatus status = ZstdFrameDecoder.Decode(
                encoded.AsSpan(0, length),
                destination,
                out _,
                out _,
                out _);
            Assert.AreNotEqual(ZstdFrameStatus.Success, status, $"prefix length {length}");
        }

        AssertDecoded(encoded, Encoding.ASCII.GetBytes("checksum protected"));

        byte[] compressed = Convert.FromBase64String(
            "KLUv/WRgAK0BAMQCdGhlIHF1aWNrIGJyb3duIGZveCBqdW1wcyBvdmVyIHRoZSBsYXp5IGRvZwoBAIx5qioDuB3+lw==");
        destination = new byte[352];
        for (int length = 0; length < compressed.Length; length++)
        {
            ZstdFrameStatus status = ZstdFrameDecoder.Decode(
                compressed.AsSpan(0, length),
                destination,
                out _,
                out _,
                out _);
            Assert.AreNotEqual(ZstdFrameStatus.Success, status, $"compressed prefix length {length}");
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD rejects block overruns and content size mismatches")]
    public void Case_V3ZstdFrame_RejectsBlockOverruns()
    {
        byte[] truncatedRaw = Frame(
            descriptor: 0x20,
            headerFields: new byte[] { 6 },
            blockAndTrailer: new byte[] { 0x31, 0x00, 0x00, 1, 2, 3, 4, 5 });
        Assert.AreEqual(
            ZstdFrameStatus.Truncated,
            ZstdFrameDecoder.Decode(truncatedRaw, new byte[6], out _, out _, out _));

        byte[] tooLargeBlock = Frame(
            descriptor: 0x00,
            headerFields: new byte[] { 0x00 },
            blockAndTrailer: BlockHeader(blockSize: 1025, blockType: 0, isLast: true));
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(tooLargeBlock, new byte[1025], out _, out _, out _));

        byte[] mismatchPayload = new byte[257];
        byte[] mismatch = Frame(
            descriptor: 0x40,
            headerFields: new byte[] { 0x00, 0x00, 0x00 }, // 1 KiB window, content size 256
            blockAndTrailer: BlockHeader(257, 0, isLast: true).Concat(mismatchPayload).ToArray());
        Assert.AreEqual(
            ZstdFrameStatus.ContentSizeMismatch,
            ZstdFrameDecoder.Decode(mismatch, new byte[257], out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD rejects malformed compressed and reserved blocks")]
    public void Case_V3ZstdFrame_RejectsMalformedCompressedAndReservedBlocks()
    {
        byte[] compressed = Frame(
            descriptor: 0x20,
            headerFields: new byte[] { 10 },
            blockAndTrailer: BlockHeader(1, blockType: 2, isLast: true).Concat(new byte[] { 0 }).ToArray());
        Assert.AreEqual(
            ZstdFrameStatus.Truncated,
            ZstdFrameDecoder.Decode(compressed, new byte[10], out int bytesConsumed, out _, out _));
        Assert.AreEqual(compressed.Length, bytesConsumed);

        byte[] reserved = Frame(
            descriptor: 0x20,
            headerFields: new byte[] { 10 },
            blockAndTrailer: BlockHeader(0, blockType: 3, isLast: true));
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(reserved, new byte[10], out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes Python compressed predefined-sequence goldens")]
    public void Case_V3ZstdFrame_DecodesPythonCompressedPredefinedGoldens()
    {
        byte[] shortSource = Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog\n", 8)));
        byte[] shortFrame = Convert.FromBase64String(
            "KLUv/WRgAK0BAMQCdGhlIHF1aWNrIGJyb3duIGZveCBqdW1wcyBvdmVyIHRoZSBsYXp5IGRvZwoBAIx5qioDuB3+lw==");
        AssertDecoded(shortFrame, shortSource);

        byte[] repeatSource = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("ABCD", 10_000)));
        byte[] repeatFrame = Convert.FromBase64String("KLUv/WRAm2UAACBBQkNEAQA5nHVHBLvuDUI=");
        AssertDecoded(repeatFrame, repeatSource);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes RLE literals and overlapping RLE sequences")]
    public void Case_V3ZstdFrame_DecodesRleLiteralsAndSequenceTables()
    {
        byte[] rleLiteralPayload =
        {
            0x29, // five regenerated RLE literals
            0xa5,
            0x00, // no sequences
        };
        AssertDecoded(
            CompressedFrameWithUnknownSize(rleLiteralPayload),
            Enumerable.Repeat((byte)0xa5, 5).ToArray(),
            hasFrameContentSize: false);

        byte[] overlappingSequencePayload =
        {
            0x08, 0x41,       // one raw literal: 'A'
            0x01,             // one sequence
            0x54,             // RLE LL/OF/ML tables
            0x01, 0x02, 0x00, // LL=1, new offset=1, ML=3
            0x04,             // two zero offset bits and reverse-stream end mark
        };
        AssertDecoded(
            CompressedFrameWithUnknownSize(overlappingSequencePayload),
            Encoding.ASCII.GetBytes("AAAA"),
            hasFrameContentSize: false);

        byte[] crossBlockSequencePayload =
        {
            0x00,             // zero raw literals
            0x01,             // one sequence
            0x54,             // RLE LL/OF/ML tables
            0x00, 0x00, 0x01, // LL=0, repeat offset 4, ML=4
            0x01,             // reverse-stream end mark
        };
        byte[] firstBlock = Encoding.ASCII.GetBytes("ABCD");
        byte[] crossBlockFrame = Frame(
            descriptor: 0x00,
            headerFields: new byte[] { 0x00 },
            blockAndTrailer: BlockHeader(firstBlock.Length, blockType: 0, isLast: false)
                .Concat(firstBlock)
                .Concat(BlockHeader(crossBlockSequencePayload.Length, blockType: 2, isLast: true))
                .Concat(crossBlockSequencePayload)
                .ToArray());
        AssertDecoded(
            crossBlockFrame,
            Encoding.ASCII.GetBytes("ABCDABCD"),
            hasFrameContentSize: false);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes direct Huffman weights in one and four streams")]
    public void Case_V3ZstdFrame_DecodesDirectHuffmanWeights()
    {
        byte[] singleStreamPayload =
        {
            0x42, 0xc0, 0x00, // four literals, three compressed bytes, single stream
            0x80, 0x10,       // one direct weight plus its implied final weight
            0x15,             // symbols 0, 1, 0, 1 and reverse-stream end mark
            0x00,             // no sequences
        };

        AssertDecoded(
            CompressedFrameWithUnknownSize(singleStreamPayload),
            new byte[] { 0, 1, 0, 1 },
            hasFrameContentSize: false);

        byte[] fourStreamPayload =
        {
            0x76, 0x00, 0x03, // seven literals, twelve compressed bytes, four streams
            0x80, 0x10,       // the same direct Huffman table
            0x01, 0x00,       // first stream length
            0x01, 0x00,       // second stream length
            0x01, 0x00,       // third stream length
            0x05, 0x06, 0x04, 0x03,
            0x00,             // no sequences
        };
        AssertDecoded(
            CompressedFrameWithUnknownSize(fourStreamPayload),
            new byte[] { 0, 1, 1, 0, 0, 0, 1 },
            hasFrameContentSize: false);

        byte[] mediumTreelessPayload =
        {
            0x7b, 0x00, 0x28, 0x00, // seven treeless literals, ten compressed bytes
            0x01, 0x00,
            0x01, 0x00,
            0x01, 0x00,
            0x05, 0x06, 0x04, 0x03,
            0x00,
        };
        byte[] longTreelessPayload =
        {
            0x7f, 0x00, 0x80, 0x02, 0x00, // the same sizes in the five-byte form
            0x01, 0x00,
            0x01, 0x00,
            0x01, 0x00,
            0x05, 0x06, 0x04, 0x03,
            0x00,
        };
        byte[] treelessFrame = Frame(
            descriptor: 0x00,
            headerFields: new byte[] { 0x00 },
            blockAndTrailer: BlockHeader(singleStreamPayload.Length, blockType: 2, isLast: false)
                .Concat(singleStreamPayload)
                .Concat(BlockHeader(mediumTreelessPayload.Length, blockType: 2, isLast: false))
                .Concat(mediumTreelessPayload)
                .Concat(BlockHeader(longTreelessPayload.Length, blockType: 2, isLast: true))
                .Concat(longTreelessPayload)
                .ToArray());
        AssertDecoded(
            treelessFrame,
            new byte[]
            {
                0, 1, 0, 1,
                0, 1, 1, 0, 0, 0, 1,
                0, 1, 1, 0, 0, 0, 1,
            },
            hasFrameContentSize: false);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes Python FSE-weight Huffman multi-block golden")]
    public void Case_V3ZstdFrame_DecodesPythonHuffmanMultiBlockGolden()
    {
        const string line = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.\n";
        byte[] source = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(line, 1_200)));
        byte[] frame = Convert.FromBase64String(
            "KLUv/aRARQIADAMAYscUFLAnHXK3JXL3idQkmzai4UhAdsgggIGycEKlQpDV6NocgUgxiyXr9VFec4UZW2cn7161POiAJy5RPs6LF9UkGodPudEA2qj4U3PkqpaneM3mBAECAAL//8hrelgwA0UAAAhtAQA8xQ6EXXyfew==");

        AssertDecoded(frame, source);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes the long sequence-count form at the block limit")]
    public void Case_V3ZstdFrame_DecodesLongSequenceCountAtBlockLimit()
    {
        const int sequenceCount = 32_768;
        byte[] payload = new byte[3 + sequenceCount + 3 + 1 + 3 + 1];
        payload[0] = 0x0c;
        payload[1] = 0x00;
        payload[2] = 0x08; // 32,768 raw literals in the three-byte form
        payload.AsSpan(3, sequenceCount).Fill((byte)'A');

        int offset = 3 + sequenceCount;
        payload[offset++] = 0xff;
        payload[offset++] = 0x00;
        payload[offset++] = 0x01; // 32,768 sequences in the long form
        payload[offset++] = 0x54; // RLE LL/OF/ML tables
        payload[offset++] = 0x01; // LL=1
        payload[offset++] = 0x00; // repeat offset 1
        payload[offset++] = 0x00; // ML=3
        payload[offset++] = 0x01; // reverse-stream end mark; all fields use zero bits
        Assert.AreEqual(payload.Length, offset);

        byte[] frame = Frame(
            descriptor: 0x00,
            headerFields: new byte[] { 0x38 }, // 128 KiB window
            blockAndTrailer: BlockHeader(payload.Length, blockType: 2, isLast: true)
                .Concat(payload)
                .ToArray());
        AssertDecoded(
            frame,
            Enumerable.Repeat((byte)'A', ZstdFrameLimits.MaximumBlockSize).ToArray(),
            hasFrameContentSize: false);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes Python compressed FSE sequence tables golden")]
    public void Case_V3ZstdFrame_DecodesPythonFseSequenceTablesGolden()
    {
        byte[] source = new byte[102_400];
        int offset = 0;
        byte[] repeated = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("0123456789abcdef", 256)));
        for (int chunk = 0; chunk < 20; chunk++)
        {
            for (int index = 0; index < 1_024; index++)
            {
                source[offset++] = (byte)((chunk * 17 + index * 31) & 0xff);
            }

            repeated.CopyTo(source, offset);
            offset += repeated.Length;
        }

        byte[] frame = Convert.FromBase64String(
            "KLUv/aQAkAEABREABBEAHz5dfJu62fgXNlV0k7LR8A8uTWyLqsnoByZFZIOiweD/Hj1ce5q52PcWNVRzkrHQ7w4tTGuKqcjnBiVEY4KhwN/+HTxbepm41/YVNFNykbDP7g0sS2qJqMfmBSRDYoGgv979HDtaeZi31vUUM1JxkK/O7QwrSmmIp8blBCNCYYCfvt38GzpZeJe21fQTMlFwj67N7AsqSWiHpsXkAyJBYH+evdz7GjlYd5a11PMSMVBvjq3M6wopSGeGpcTjAiFAX36dvNv6GThXdpW00/IRME9ujazL6gkoR2aFpMPiASA/Xn2cu9r5GDdWdZSz0vEQL05tjKvK6QgnRmWEo8LhMDEyMzQ1Njc4OWFiY2RlZlGo8etf+xkRTBIUAQRxM3YS+AiB6QRHBD9E8A3EQEABZOAH83f758ehNBSCNt3r7R85gSxo6L6tO3vmr/bPD/NoKPRJNNXaP3oCZdLckWZAw4mffv5q//xQaUhwF1957Z8+wf7TEFgde/Gav9s/Pw6goagXcKBq/4gJLEZDJ+nT252/2z8/xqS5B81YGD22qP2zJ3jTED7ffnTmz/bPD4FoKLLNO5vbP2oCt9LQLbz4ceav9s+PNtFQoKq1WO0fbQJh0txAM+CRKTczf7V/fpQ0FMjHH1n7p59gJTSEDH78nPlX++dnWdFQH7LlDu0fOQFtaOj9teyk+bP988OZNGaXNGMl7R8LFaDfwAmrAktesEU=");

        AssertDecoded(frame, source);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD decodes treeless literals and repeat sequence tables across blocks")]
    public void Case_V3ZstdFrame_DecodesTreelessAndRepeatModesAcrossBlocks()
    {
        byte[] source = BuildDeterministicWordCorpus(50_000);
        byte[] frame = Convert.FromBase64String(
            "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");

        byte[] decoded = new byte[source.Length];
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameDecoder.Decode(frame, decoded, out int bytesConsumed, out int bytesWritten, out ZstdFrameHeader header));
        Assert.AreEqual(frame.Length, bytesConsumed);
        Assert.AreEqual(source.Length, bytesWritten);
        Assert.IsFalse(header.IsSingleSegment);
        Assert.IsFalse(header.HasFrameContentSize);
        Assert.AreEqual(16_384UL, header.WindowSize);
        CollectionAssert.AreEqual(source, decoded);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD accepts libzstd one-shot empty-RLE compatibility form")]
    public void Case_V3ZstdFrame_AcceptsZeroWindowEmptyRleCompatibilityFrame()
    {
        byte[] emptyRle = Frame(
            descriptor: 0x20,
            headerFields: new byte[] { 0 },
            blockAndTrailer: BlockHeader(0, blockType: 1, isLast: true).Concat(new byte[] { 0xa5 }).ToArray());

        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameDecoder.Decode(
                emptyRle,
                Span<byte>.Empty,
                out int bytesConsumed,
                out int bytesWritten,
                out ZstdFrameHeader header));
        Assert.AreEqual(emptyRle.Length, bytesConsumed);
        Assert.AreEqual(0, bytesWritten);
        Assert.AreEqual(0UL, header.WindowSize);
        Assert.AreEqual(0UL, header.FrameContentSize);

        byte[] truncatedRle = emptyRle.Take(emptyRle.Length - 1).ToArray();
        Assert.AreEqual(
            ZstdFrameStatus.Truncated,
            ZstdFrameDecoder.Decode(
                truncatedRle,
                Span<byte>.Empty,
                out bytesConsumed,
                out bytesWritten,
                out _));
        Assert.AreEqual(9, bytesConsumed);
        Assert.AreEqual(0, bytesWritten);
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD compressed frame consumes checksum before the next frame")]
    public void Case_V3ZstdFrame_CompressedFrameConsumesChecksumTrailer()
    {
        byte[] compressedSource = Encoding.ASCII.GetBytes(
            string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog\n", 8)));
        byte[] compressedFrame = Convert.FromBase64String(
            "KLUv/WRgAK0BAMQCdGhlIHF1aWNrIGJyb3duIGZveCBqdW1wcyBvdmVyIHRoZSBsYXp5IGRvZwoBAIx5qioDuB3+lw==");
        byte[] nextSource = Encoding.ASCII.GetBytes("next");
        byte[] nextFrame = Encode(nextSource, includeChecksum: false);
        byte[] concatenated = compressedFrame.Concat(nextFrame).ToArray();

        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameDecoder.Decode(
                concatenated,
                new byte[compressedSource.Length],
                out int bytesConsumed,
                out int compressedBytesWritten,
                out _));
        Assert.AreEqual(compressedFrame.Length, bytesConsumed);
        Assert.AreEqual(compressedSource.Length, compressedBytesWritten);

        byte[] decodedNext = new byte[nextSource.Length];
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameDecoder.Decode(
                concatenated.AsSpan(bytesConsumed),
                decodedNext,
                out int nextBytesConsumed,
                out int nextBytesWritten,
                out _));
        Assert.AreEqual(nextFrame.Length, nextBytesConsumed);
        Assert.AreEqual(nextSource.Length, nextBytesWritten);
        CollectionAssert.AreEqual(nextSource, decodedNext);

        for (int checksumBytes = 0; checksumBytes < sizeof(uint); checksumBytes++)
        {
            int truncatedLength = compressedFrame.Length - sizeof(uint) + checksumBytes;
            Assert.AreEqual(
                ZstdFrameStatus.Truncated,
                ZstdFrameDecoder.Decode(
                    compressedFrame.AsSpan(0, truncatedLength),
                    new byte[compressedSource.Length],
                    out _,
                    out _,
                    out _),
                $"checksum bytes present: {checksumBytes}");
        }
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD rejects malformed compressed tables and bitstream end marks")]
    public void Case_V3ZstdFrame_RejectsBadCompressedTablesAndEndMarks()
    {
        byte[] badTablePayload =
        {
            0x00,       // zero raw literals
            0x01,       // one sequence
            0x80,       // compressed LL table, predefined OF/ML tables
            0x0f,       // invalid LL tableLog 20
            0x01,
        };
        byte[] badTableFrame = Frame(
            descriptor: 0x00,
            headerFields: new byte[] { 0x00 },
            blockAndTrailer: BlockHeader(badTablePayload.Length, blockType: 2, isLast: true)
                .Concat(badTablePayload)
                .ToArray());
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(badTableFrame, new byte[1_024], out _, out _, out _));

        byte[] treelessWithoutTablePayload =
        {
            0x43, 0x40, 0x00, // four treeless literals in one compressed byte
            0x01,
            0x00,
        };
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(
                CompressedFrameWithUnknownSize(treelessWithoutTablePayload),
                new byte[4],
                out _,
                out _,
                out _));

        byte[] repeatTableWithoutPredecessorPayload =
        {
            0x00, // zero raw literals
            0x01, // one sequence
            0xc0, // repeat LL table, predefined OF/ML tables
        };
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(
                CompressedFrameWithUnknownSize(repeatTableWithoutPredecessorPayload),
                new byte[4],
                out _,
                out _,
                out _));

        byte[] badEndMark = Convert.FromBase64String(
            "KLUv/WRgAK0BAMQCdGhlIHF1aWNrIGJyb3duIGZveCBqdW1wcyBvdmVyIHRoZSBsYXp5IGRvZwoBAIx5qioDuB3+lw==");
        int compressedPayloadEnd = badEndMark.Length - sizeof(uint) - 1;
        badEndMark[compressedPayloadEnd] = 0;
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(badEndMark, new byte[352], out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD rejects sequence output and history overruns")]
    public void Case_V3ZstdFrame_RejectsSequenceAndHistoryOverruns()
    {
        byte[] outputOverrunPayload =
        {
            0x00,             // zero raw literals
            0x01,             // one sequence
            0x54,             // RLE LL/OF/ML tables
            0x00, 0x00, 0x34, // LL=0, OF=0, ML=52
            0x00, 0x00, 0x01, // sixteen zero extra bits and end mark
        };
        byte[] outputOverrun = CompressedFrameWithUnknownSize(outputOverrunPayload);
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(outputOverrun, new byte[1_024], out _, out _, out _));

        byte[] badOffsetPayload =
        {
            0x00,             // zero raw literals
            0x01,             // one sequence
            0x54,             // RLE LL/OF/ML tables
            0x00, 0x00, 0x00, // LL=0, OF=0 => repeat offset 4, ML=3
            0x01,             // end mark
        };
        byte[] badOffset = CompressedFrameWithUnknownSize(badOffsetPayload);
        Assert.AreEqual(
            ZstdFrameStatus.Corrupt,
            ZstdFrameDecoder.Decode(badOffset, new byte[1_024], out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD detects compressed content-size mismatch")]
    public void Case_V3ZstdFrame_CompressedContentSizeMismatch()
    {
        byte[] literalPayload = new byte[260];
        literalPayload[0] = 0x14; // raw literals, 12-bit size representation
        literalPayload[1] = 0x10; // regenerated size 257
        for (int index = 0; index < 257; index++)
        {
            literalPayload[2 + index] = (byte)index;
        }

        literalPayload[^1] = 0; // no sequences
        byte[] frame = Frame(
            descriptor: 0x40,
            headerFields: new byte[] { 0x00, 0x00, 0x00 }, // 1 KiB window, FCS 256
            blockAndTrailer: BlockHeader(literalPayload.Length, blockType: 2, isLast: true)
                .Concat(literalPayload)
                .ToArray());

        Assert.AreEqual(
            ZstdFrameStatus.ContentSizeMismatch,
            ZstdFrameDecoder.Decode(frame, new byte[257], out _, out _, out _));
    }

    [TestMethod(DisplayName = "[TinyEXR.NET Test] V3 ZSTD reports insufficient output buffers without partial encoding")]
    public void Case_V3ZstdFrame_DestinationBounds()
    {
        byte[] source = Encoding.ASCII.GetBytes("bounded");
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdRawRleEncoder.GetEncodedSize(source, includeChecksum: true, out int encodedSize));

        byte[] encoded = Enumerable.Repeat((byte)0xcc, encodedSize - 1).ToArray();
        Assert.AreEqual(
            ZstdFrameStatus.DestinationTooSmall,
            ZstdRawRleEncoder.Encode(source, encoded, includeChecksum: true, out int bytesWritten));
        Assert.AreEqual(0, bytesWritten);
        Assert.IsTrue(encoded.All(static value => value == 0xcc));

        byte[] valid = Encode(source, includeChecksum: false);
        Assert.AreEqual(
            ZstdFrameStatus.DestinationTooSmall,
            ZstdFrameDecoder.Decode(valid, new byte[source.Length - 1], out _, out _, out _));
    }

    private static byte[] Encode(byte[] source, bool includeChecksum)
    {
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdRawRleEncoder.GetEncodedSize(source, includeChecksum, out int encodedSize));
        byte[] encoded = new byte[encodedSize];
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdRawRleEncoder.Encode(source, encoded, includeChecksum, out int bytesWritten));
        Assert.AreEqual(encoded.Length, bytesWritten);
        return encoded;
    }

    private static byte[] BuildDeterministicWordCorpus(int size)
    {
        string[] words =
        {
            "lorem", "ipsum", "dolor", "sit", "amet", "consectetur",
            "adipiscing", "elit", "sed", "do", "eiusmod", "tempor",
        };
        List<byte> bytes = new(size + 256);
        uint state = 1;
        while (bytes.Count < size)
        {
            state = unchecked((state * 1_664_525U) + 1_013_904_223U);
            int wordCount = 8 + (int)(state % 16);
            for (int index = 0; index < wordCount; index++)
            {
                state = unchecked((state * 1_664_525U) + 1_013_904_223U);
                bytes.AddRange(Encoding.ASCII.GetBytes(words[state % (uint)words.Length]));
                bytes.AddRange(index + 1 < wordCount
                    ? new byte[] { (byte)' ' }
                    : new byte[] { (byte)'.', (byte)'\n' });
            }
        }

        return bytes.Take(size).ToArray();
    }

    private static void AssertDecoded(byte[] encoded, byte[] expected, bool hasFrameContentSize = true)
    {
        byte[] decoded = new byte[expected.Length];
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameDecoder.Decode(
                encoded,
                decoded,
                out int bytesConsumed,
                out int bytesWritten,
                out ZstdFrameHeader header));
        Assert.AreEqual(encoded.Length, bytesConsumed);
        Assert.AreEqual(expected.Length, bytesWritten);
        Assert.AreEqual(hasFrameContentSize, header.IsSingleSegment);
        Assert.AreEqual(hasFrameContentSize, header.HasFrameContentSize);
        if (hasFrameContentSize)
        {
            Assert.AreEqual((ulong)expected.Length, header.FrameContentSize);
        }

        CollectionAssert.AreEqual(expected, decoded);
    }

    private static void AssertHeaderContentSize(
        byte[] source,
        ulong expectedContentSize,
        ulong expectedWindowSize,
        int expectedHeaderSize)
    {
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameHeaderParser.Parse(source, out ZstdFrameHeader header, out int bytesConsumed));
        Assert.IsTrue(header.HasFrameContentSize);
        Assert.AreEqual(expectedContentSize, header.FrameContentSize);
        Assert.AreEqual(expectedWindowSize, header.WindowSize);
        Assert.AreEqual(expectedHeaderSize, header.HeaderSize);
        Assert.AreEqual(expectedHeaderSize, bytesConsumed);
    }

    private static void AssertDictionaryField(byte descriptor, byte[] fields, uint expectedDictionaryId)
    {
        Assert.AreEqual(
            ZstdFrameStatus.Success,
            ZstdFrameHeaderParser.Parse(Header(descriptor, fields), out ZstdFrameHeader header, out _));
        Assert.IsTrue(header.HasDictionaryId);
        Assert.AreEqual(expectedDictionaryId, header.DictionaryId);
    }

    private static byte[] Header(byte descriptor, byte[] fields)
    {
        return Frame(descriptor, fields, Array.Empty<byte>());
    }

    private static byte[] Frame(byte descriptor, byte[] headerFields, byte[] blockAndTrailer)
    {
        byte[] result = new byte[5 + headerFields.Length + blockAndTrailer.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, ZstdFrameHeaderParser.ZstandardMagic);
        result[4] = descriptor;
        headerFields.CopyTo(result, 5);
        blockAndTrailer.CopyTo(result, 5 + headerFields.Length);
        return result;
    }

    private static byte[] BlockHeader(int blockSize, int blockType, bool isLast)
    {
        uint value = checked(((uint)blockSize << 3) | ((uint)blockType << 1) | (isLast ? 1U : 0U));
        return new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16) };
    }

    private static byte[] CompressedFrameWithUnknownSize(byte[] payload)
    {
        return Frame(
            descriptor: 0x00,
            headerFields: new byte[] { 0x00 },
            blockAndTrailer: BlockHeader(payload.Length, blockType: 2, isLast: true)
                .Concat(payload)
                .ToArray());
    }
}
