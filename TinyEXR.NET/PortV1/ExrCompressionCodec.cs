using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
#if NET10_0_OR_GREATER
using System.IO.Compression;
#endif

namespace TinyEXR.PortV1
{
    internal static class ExrCompressionCodec
    {
        private const int MinRunLength = 3;
        private const int MaxRunLength = 127;

        private const int HufEncBits = 16;
        private const int HufDecBits = 14;
        private const int HufEncSize = (1 << HufEncBits) + 1;
        private const int HufDecSize = 1 << HufDecBits;
        private const int HufDecMask = HufDecSize - 1;
        private const int ShortZeroCodeRun = 59;
        private const int LongZeroCodeRun = 63;
        private const int ShortestLongRun = 2 + LongZeroCodeRun - ShortZeroCodeRun;
        private const int LongestLongRun = 255 + ShortestLongRun;
        private const int UShortRange = 1 << 16;
        private const int BitmapSize = UShortRange >> 3;

        private static readonly object B44TableLock = new object();
        private static readonly ushort[] B44ExpTable = new ushort[UShortRange];
        private static readonly ushort[] B44LogTable = new ushort[UShortRange];
        private static bool s_b44TablesInitialized;

        private readonly struct ChannelLayout
        {
            public ChannelLayout(ExrPixelType type, int sampleSize, int offsetBytes, int samplingX, int samplingY, byte linear)
            {
                Type = type;
                SampleSize = sampleSize;
                OffsetBytes = offsetBytes;
                SamplingX = samplingX;
                SamplingY = samplingY;
                Linear = linear;
            }

            public ExrPixelType Type { get; }

            public int SampleSize { get; }

            public int OffsetBytes { get; }

            public int SamplingX { get; }

            public int SamplingY { get; }

            public byte Linear { get; }

            public int WordSize => SampleSize >> 1;

            public bool HasSubsampling => SamplingX != 1 || SamplingY != 1;
        }

        private sealed class HufDecEntry
        {
            public byte Length;
            public int Literal;
            public int[]? Symbols;
        }

        private sealed class BitWriter
        {
            private readonly List<byte> _bytes = new List<byte>();
            private ulong _buffer;
            private int _bitCount;
            private int _totalBits;

            public int TotalBits => _totalBits;

            public void WriteBits(int bitCount, ulong bits)
            {
                _buffer <<= bitCount;
                _bitCount += bitCount;
                _buffer |= bits;
                _totalBits += bitCount;

                while (_bitCount >= 8)
                {
                    _bytes.Add((byte)(_buffer >> (_bitCount - 8)));
                    _bitCount -= 8;
                }
            }

            public byte[] ToArray()
            {
                if (_bitCount > 0)
                {
                    _bytes.Add((byte)(_buffer << (8 - _bitCount)));
                    _bitCount = 0;
                }

                return _bytes.ToArray();
            }
        }

        private sealed class BitReader
        {
            private readonly byte[] _bytes;
            private readonly int _end;
            private int _offset;
            private ulong _buffer;
            private int _bitCount;

            public BitReader(byte[] bytes, int offset, int length)
            {
                _bytes = bytes;
                _offset = offset;
                _end = offset + length;
            }

            public bool TryReadBits(int bitCount, out int value)
            {
                while (_bitCount < bitCount)
                {
                    if (_offset >= _end)
                    {
                        value = 0;
                        return false;
                    }

                    _buffer = (_buffer << 8) | _bytes[_offset++];
                    _bitCount += 8;
                }

                _bitCount -= bitCount;
                value = (int)((_buffer >> _bitCount) & ((1UL << bitCount) - 1));
                return true;
            }
        }

        public static ResultCode TryEncodePayload(
            CompressionType compression,
            IList<ExrChannel> channels,
            int width,
            int height,
            byte[] raw,
            out byte[] payload)
        {
            payload = raw;

            if (!TryBuildLayouts(channels, out ChannelLayout[] layouts, out int pixelStride))
            {
                return ResultCode.UnsupportedFeature;
            }

            if (HasSubsampledChannels(layouts))
            {
                return ResultCode.UnsupportedFeature;
            }

            if (raw.Length != checked(width * height * pixelStride))
            {
                return ResultCode.InvalidArgument;
            }

            switch (compression)
            {
                case CompressionType.None:
                    return ResultCode.Success;
                case CompressionType.RLE:
                    payload = CompressRle(raw);
                    return ResultCode.Success;
                case CompressionType.ZIP:
                case CompressionType.ZIPS:
                    return TryCompressZip(raw, out payload);
                case CompressionType.PIZ:
                    return TryCompressPiz(layouts, width, height, raw, out payload);
                case CompressionType.PXR24:
                    return TryCompressPxr24(layouts, pixelStride, width, height, raw, out payload);
                case CompressionType.B44:
                case CompressionType.B44A:
                    return TryCompressB44(layouts, pixelStride, width, height, raw, compression == CompressionType.B44A, out payload);
                default:
                    return ResultCode.UnsupportedFeature;
            }
        }

        public static ResultCode TryDecodePayload(
            CompressionType compression,
            IList<ExrChannel> channels,
            int startX,
            int startY,
            int width,
            int height,
            ReadOnlySpan<byte> payload,
            int expectedSize,
            out byte[] raw)
        {
            raw = Array.Empty<byte>();

            if (!TryBuildLayouts(channels, out ChannelLayout[] layouts, out int pixelStride))
            {
                return ResultCode.UnsupportedFeature;
            }

            if (!TryCalculateDecodedSize(layouts, startX, startY, width, height, out int decodedSize))
            {
                return ResultCode.InvalidArgument;
            }

            if (expectedSize != decodedSize)
            {
                return ResultCode.InvalidArgument;
            }

            switch (compression)
            {
                case CompressionType.None:
                    if (payload.Length != expectedSize)
                    {
                        return ResultCode.InvalidData;
                    }

                    raw = payload.ToArray();
                    return ResultCode.Success;
                case CompressionType.RLE:
                    return TryDecompressRle(payload, expectedSize, out raw);
                case CompressionType.ZIP:
                case CompressionType.ZIPS:
                    return TryDecompressZip(payload, expectedSize, out raw);
                case CompressionType.PIZ:
                    return TryDecompressPiz(layouts, startX, startY, width, height, payload, expectedSize, out raw);
                case CompressionType.PXR24:
                    if (HasSubsampledChannels(layouts))
                    {
                        return ResultCode.UnsupportedFeature;
                    }

                    return TryDecompressPxr24(layouts, pixelStride, width, height, payload, expectedSize, out raw);
                case CompressionType.B44:
                case CompressionType.B44A:
                    if (HasSubsampledChannels(layouts))
                    {
                        return ResultCode.UnsupportedFeature;
                    }

                    return TryDecompressB44(layouts, pixelStride, width, height, payload, expectedSize, out raw);
                default:
                    return ResultCode.UnsupportedFeature;
            }
        }

        private static bool TryBuildLayouts(IList<ExrChannel> channels, out ChannelLayout[] layouts, out int pixelStride)
        {
            layouts = new ChannelLayout[channels.Count];
            pixelStride = 0;

            for (int i = 0; i < channels.Count; i++)
            {
                int sampleSize = Exr.TypeSize(channels[i].Type);
                if ((sampleSize != 2 && sampleSize != 4) || channels[i].SamplingX <= 0 || channels[i].SamplingY <= 0)
                {
                    layouts = Array.Empty<ChannelLayout>();
                    pixelStride = 0;
                    return false;
                }

                layouts[i] = new ChannelLayout(channels[i].Type, sampleSize, pixelStride, channels[i].SamplingX, channels[i].SamplingY, channels[i].Linear);
                pixelStride += sampleSize;
            }

            return true;
        }

        private static bool HasSubsampledChannels(ChannelLayout[] layouts)
        {
            for (int i = 0; i < layouts.Length; i++)
            {
                if (layouts[i].HasSubsampling)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountSamplePositions(int start, int size, int sampling)
        {
            if (size <= 0)
            {
                return 0;
            }

            int remainder = start % sampling;
            if (remainder < 0)
            {
                remainder += sampling;
            }

            int firstOffset = remainder == 0 ? 0 : sampling - remainder;
            if (firstOffset >= size)
            {
                return 0;
            }

            return ((size - 1 - firstOffset) / sampling) + 1;
        }

        private static bool IsSampledCoordinate(int coordinate, int sampling)
        {
            int remainder = coordinate % sampling;
            return remainder == 0;
        }

        private static bool TryCalculateDecodedSize(ChannelLayout[] layouts, int startX, int startY, int width, int height, out int decodedSize)
        {
            decodedSize = 0;
            try
            {
                for (int i = 0; i < layouts.Length; i++)
                {
                    int sampledWidth = CountSamplePositions(startX, width, layouts[i].SamplingX);
                    int sampledHeight = CountSamplePositions(startY, height, layouts[i].SamplingY);
                    decodedSize = checked(decodedSize + checked(checked(sampledWidth * sampledHeight) * layouts[i].SampleSize));
                }

                return true;
            }
            catch (OverflowException)
            {
                decodedSize = 0;
                return false;
            }
        }

        private static byte[] ApplyExrPredictorAndReorder(ReadOnlySpan<byte> raw)
        {
            byte[] tmp = new byte[raw.Length];
            int half = (raw.Length + 1) / 2;
            int targetA = 0;
            int targetB = half;
            for (int i = 0; i < raw.Length; i += 2)
            {
                tmp[targetA++] = raw[i];
                if (i + 1 < raw.Length)
                {
                    tmp[targetB++] = raw[i + 1];
                }
            }

            int previous = tmp.Length == 0 ? 0 : tmp[0];
            for (int i = 1; i < tmp.Length; i++)
            {
                int current = tmp[i];
                tmp[i] = unchecked((byte)(current - previous + 384));
                previous = current;
            }

            return tmp;
        }

        private static byte[] UndoExrPredictorAndReorder(ReadOnlySpan<byte> payload, int expectedSize)
        {
            byte[] tmp = payload.ToArray();
            for (int i = 1; i < tmp.Length; i++)
            {
                tmp[i] = unchecked((byte)(tmp[i - 1] + tmp[i] - 128));
            }

            byte[] raw = new byte[expectedSize];
            int half = (expectedSize + 1) / 2;
            int sourceA = 0;
            int sourceB = half;
            for (int i = 0; i < expectedSize; i += 2)
            {
                raw[i] = tmp[sourceA++];
                if (i + 1 < expectedSize)
                {
                    raw[i + 1] = tmp[sourceB++];
                }
            }

            return raw;
        }

#if NET10_0_OR_GREATER
        private static ResultCode TryCompressZip(ReadOnlySpan<byte> raw, out byte[] payload)
        {
            byte[] tmp = ApplyExrPredictorAndReorder(raw);

            try
            {
                using MemoryStream output = new MemoryStream();
                using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(tmp, 0, tmp.Length);
                }

                payload = output.ToArray();
                if (payload.Length >= raw.Length)
                {
                    payload = raw.ToArray();
                }

                return ResultCode.Success;
            }
            catch
            {
                payload = Array.Empty<byte>();
                return ResultCode.SerialzationFailed;
            }
        }

        private static ResultCode TryDecompressZip(ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            if (payload.Length == expectedSize)
            {
                raw = payload.ToArray();
                return ResultCode.Success;
            }

            try
            {
                using MemoryStream input = new MemoryStream(payload.ToArray(), writable: false);
                using ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress);
                using MemoryStream output = new MemoryStream();
                zlib.CopyTo(output);
                byte[] tmp = output.ToArray();
                if (tmp.Length != expectedSize)
                {
                    raw = Array.Empty<byte>();
                    return ResultCode.InvalidData;
                }

                raw = UndoExrPredictorAndReorder(tmp, expectedSize);
                return ResultCode.Success;
            }
            catch
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }
        }

        private static ResultCode TryCompressZlib(ReadOnlySpan<byte> raw, out byte[] payload)
        {
            try
            {
                using MemoryStream output = new MemoryStream();
                using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(raw);
                }

                payload = output.ToArray();
                return ResultCode.Success;
            }
            catch
            {
                payload = Array.Empty<byte>();
                return ResultCode.SerialzationFailed;
            }
        }

        private static ResultCode TryDecompressZlib(ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            if (payload.Length == expectedSize)
            {
                raw = payload.ToArray();
                return ResultCode.Success;
            }

            try
            {
                using MemoryStream input = new MemoryStream(payload.ToArray(), writable: false);
                using ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress);
                using MemoryStream output = new MemoryStream();
                zlib.CopyTo(output);
                raw = output.ToArray();
                return raw.Length == expectedSize ? ResultCode.Success : ResultCode.InvalidData;
            }
            catch
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }
        }
#else
        private static ResultCode TryCompressZip(ReadOnlySpan<byte> raw, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            return ResultCode.UnsupportedFeature;
        }

        private static ResultCode TryDecompressZip(ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            raw = Array.Empty<byte>();
            return ResultCode.UnsupportedFeature;
        }

        private static ResultCode TryCompressZlib(ReadOnlySpan<byte> raw, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            return ResultCode.UnsupportedFeature;
        }

        private static ResultCode TryDecompressZlib(ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            raw = Array.Empty<byte>();
            return ResultCode.UnsupportedFeature;
        }
#endif

        private static byte[] CompressRle(ReadOnlySpan<byte> raw)
        {
            byte[] tmp = ApplyExrPredictorAndReorder(raw);
            List<byte> output = new List<byte>(Math.Max(1, tmp.Length * 3 / 2));

            int runStart = 0;
            int runEnd = 1;
            while (runStart < tmp.Length)
            {
                while (runEnd < tmp.Length && tmp[runStart] == tmp[runEnd] && runEnd - runStart - 1 < MaxRunLength)
                {
                    runEnd++;
                }

                if (runEnd - runStart >= MinRunLength)
                {
                    output.Add((byte)(runEnd - runStart - 1));
                    output.Add(tmp[runStart]);
                    runStart = runEnd;
                }
                else
                {
                    while (runEnd < tmp.Length &&
                        ((runEnd + 1 >= tmp.Length || tmp[runEnd] != tmp[runEnd + 1]) ||
                         (runEnd + 2 >= tmp.Length || tmp[runEnd + 1] != tmp[runEnd + 2])) &&
                        runEnd - runStart < MaxRunLength)
                    {
                        runEnd++;
                    }

                    output.Add(unchecked((byte)(runStart - runEnd)));
                    while (runStart < runEnd)
                    {
                        output.Add(tmp[runStart++]);
                    }
                }

                runEnd++;
            }

            byte[] compressed = output.ToArray();
            return compressed.Length >= raw.Length ? raw.ToArray() : compressed;
        }

        private static ResultCode TryDecompressRle(ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            if (payload.Length == expectedSize)
            {
                raw = payload.ToArray();
                return ResultCode.Success;
            }

            if (payload.Length <= 2)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            byte[] tmp = new byte[expectedSize];
            int source = 0;
            int destination = 0;
            while (source < payload.Length)
            {
                int control = (sbyte)payload[source++];
                if (control < 0)
                {
                    int count = -control;
                    if (source + count > payload.Length || destination + count > tmp.Length)
                    {
                        raw = Array.Empty<byte>();
                        return ResultCode.InvalidData;
                    }

                    payload.Slice(source, count).CopyTo(tmp.AsSpan(destination, count));
                    source += count;
                    destination += count;
                }
                else
                {
                    int count = control + 1;
                    if (source >= payload.Length || destination + count > tmp.Length)
                    {
                        raw = Array.Empty<byte>();
                        return ResultCode.InvalidData;
                    }

                    tmp.AsSpan(destination, count).Fill(payload[source++]);
                    destination += count;
                }
            }

            if (destination != expectedSize)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            raw = UndoExrPredictorAndReorder(tmp, expectedSize);
            return ResultCode.Success;
        }

        private static ResultCode TryCompressPiz(ChannelLayout[] layouts, int width, int height, byte[] raw, out byte[] payload)
        {
            ushort[] tmpBuffer = new ushort[raw.Length / sizeof(ushort)];
            PizChannelData[] channelData = new PizChannelData[layouts.Length];
            int cursor = 0;
            for (int i = 0; i < layouts.Length; i++)
            {
                channelData[i] = new PizChannelData(cursor, width, height, layouts[i].WordSize);
                cursor += width * height * layouts[i].WordSize;
            }

            int pixelStride = 0;
            for (int i = 0; i < layouts.Length; i++)
            {
                pixelStride += layouts[i].SampleSize;
            }

            int[] channelPositions = new int[layouts.Length];
            for (int y = 0; y < height; y++)
            {
                int rowBase = y * width * pixelStride;
                for (int channelIndex = 0; channelIndex < layouts.Length; channelIndex++)
                {
                    int rowOffset = rowBase + layouts[channelIndex].OffsetBytes * width;
                    int rowWords = width * layouts[channelIndex].WordSize;
                    for (int i = 0; i < rowWords; i++)
                    {
                        tmpBuffer[channelData[channelIndex].Start + channelPositions[channelIndex]++] =
                            BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(rowOffset + i * sizeof(ushort), sizeof(ushort)));
                    }
                }
            }

            byte[] bitmap = new byte[BitmapSize];
            BitmapFromData(tmpBuffer, bitmap, out ushort minNonZero, out ushort maxNonZero);
            ushort[] forwardLut = new ushort[UShortRange];
            ushort maxValue = ForwardLutFromBitmap(bitmap, forwardLut);
            ApplyLut(forwardLut, tmpBuffer);

            for (int i = 0; i < channelData.Length; i++)
            {
                for (int plane = 0; plane < channelData[i].Size; plane++)
                {
                    Wav2Encode(tmpBuffer, channelData[i].Start + plane, channelData[i].Nx, channelData[i].Size, channelData[i].Ny, channelData[i].Nx * channelData[i].Size, maxValue);
                }
            }

            byte[] huffman = HufCompress(tmpBuffer);
            using MemoryStream output = new MemoryStream();
            Span<byte> header = stackalloc byte[sizeof(ushort) * 2];
            BinaryPrimitives.WriteUInt16LittleEndian(header, minNonZero);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(sizeof(ushort)), maxNonZero);
            output.Write(header);
            if (minNonZero <= maxNonZero)
            {
                output.Write(bitmap, minNonZero, maxNonZero - minNonZero + 1);
            }

            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, huffman.Length);
            output.Write(length);
            output.Write(huffman, 0, huffman.Length);

            payload = output.ToArray();
            if (payload.Length >= raw.Length)
            {
                payload = raw.ToArray();
            }

            return ResultCode.Success;
        }

        private static ResultCode TryDecompressPiz(ChannelLayout[] layouts, int startX, int startY, int width, int height, ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            if (payload.Length == expectedSize)
            {
                raw = payload.ToArray();
                return ResultCode.Success;
            }

            if ((expectedSize & 1) != 0 || payload.Length < sizeof(ushort) * 2 + sizeof(int))
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            byte[] bitmap = new byte[BitmapSize];
            int offset = 0;
            ushort minNonZero = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            ushort maxNonZero = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            if (maxNonZero >= BitmapSize)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            if (minNonZero <= maxNonZero)
            {
                int bitmapLength = maxNonZero - minNonZero + 1;
                if (offset + bitmapLength > payload.Length)
                {
                    raw = Array.Empty<byte>();
                    return ResultCode.InvalidData;
                }

                payload.Slice(offset, bitmapLength).CopyTo(bitmap.AsSpan(minNonZero, bitmapLength));
                offset += bitmapLength;
            }
            else if (!(minNonZero == BitmapSize - 1 && maxNonZero == 0))
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            if (offset + sizeof(int) > payload.Length)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            int huffmanLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, sizeof(int)));
            offset += sizeof(int);
            if (huffmanLength < 0 || offset + huffmanLength > payload.Length)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            ushort[] reverseLut = new ushort[UShortRange];
            ushort maxValue = ReverseLutFromBitmap(bitmap, reverseLut);
            if (!TryHufUncompress(payload.Slice(offset, huffmanLength).ToArray(), expectedSize / sizeof(ushort), out ushort[] tmpBuffer))
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            PizChannelData[] channelData = new PizChannelData[layouts.Length];
            int cursor = 0;
            for (int i = 0; i < layouts.Length; i++)
            {
                int sampledWidth = CountSamplePositions(startX, width, layouts[i].SamplingX);
                int sampledHeight = CountSamplePositions(startY, height, layouts[i].SamplingY);
                channelData[i] = new PizChannelData(cursor, sampledWidth, sampledHeight, layouts[i].WordSize);
                cursor += sampledWidth * sampledHeight * layouts[i].WordSize;
            }

            if (cursor != expectedSize / sizeof(ushort))
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            for (int i = 0; i < channelData.Length; i++)
            {
                for (int plane = 0; plane < channelData[i].Size; plane++)
                {
                    Wav2Decode(tmpBuffer, channelData[i].Start + plane, channelData[i].Nx, channelData[i].Size, channelData[i].Ny, channelData[i].Nx * channelData[i].Size, maxValue);
                }
            }

            ApplyLut(reverseLut, tmpBuffer);

            raw = new byte[expectedSize];
            int[] channelPositions = new int[layouts.Length];
            int rawOffset = 0;
            for (int y = 0; y < height; y++)
            {
                int absoluteY = startY + y;
                for (int channelIndex = 0; channelIndex < layouts.Length; channelIndex++)
                {
                    if (!IsSampledCoordinate(absoluteY, layouts[channelIndex].SamplingY))
                    {
                        continue;
                    }

                    int rowWords = channelData[channelIndex].Nx * layouts[channelIndex].WordSize;
                    for (int i = 0; i < rowWords; i++)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            raw.AsSpan(rawOffset + i * sizeof(ushort), sizeof(ushort)),
                            tmpBuffer[channelData[channelIndex].Start + channelPositions[channelIndex]++]);
                    }

                    rawOffset += rowWords * sizeof(ushort);
                }
            }

            if (rawOffset != raw.Length)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            return ResultCode.Success;
        }

        private static ResultCode TryCompressPxr24(ChannelLayout[] layouts, int pixelStride, int width, int height, byte[] raw, out byte[] payload)
        {
            int packedSize = 0;
            for (int i = 0; i < layouts.Length; i++)
            {
                packedSize += width * height * (layouts[i].Type == ExrPixelType.Float ? 3 : layouts[i].SampleSize);
            }

            byte[] packed = new byte[packedSize];
            int packedOffset = 0;
            for (int line = 0; line < height; line++)
            {
                int rowBase = line * width * pixelStride;
                for (int channelIndex = 0; channelIndex < layouts.Length; channelIndex++)
                {
                    ChannelLayout layout = layouts[channelIndex];
                    int source = rowBase + layout.OffsetBytes * width;
                    if (layout.Type == ExrPixelType.UInt)
                    {
                        int plane0 = packedOffset;
                        int plane1 = plane0 + width;
                        int plane2 = plane1 + width;
                        int plane3 = plane2 + width;
                        packedOffset += width * 4;

                        uint previous = 0;
                        for (int x = 0; x < width; x++)
                        {
                            uint pixel = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(source + x * 4, 4));
                            uint diff = pixel - previous;
                            previous = pixel;
                            packed[plane0 + x] = (byte)(diff >> 24);
                            packed[plane1 + x] = (byte)(diff >> 16);
                            packed[plane2 + x] = (byte)(diff >> 8);
                            packed[plane3 + x] = (byte)diff;
                        }
                    }
                    else if (layout.Type == ExrPixelType.Half)
                    {
                        int plane0 = packedOffset;
                        int plane1 = plane0 + width;
                        packedOffset += width * 2;

                        uint previous = 0;
                        for (int x = 0; x < width; x++)
                        {
                            uint pixel = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(source + x * 2, 2));
                            uint diff = pixel - previous;
                            previous = pixel;
                            packed[plane0 + x] = (byte)(diff >> 8);
                            packed[plane1 + x] = (byte)diff;
                        }
                    }
                    else
                    {
                        int plane0 = packedOffset;
                        int plane1 = plane0 + width;
                        int plane2 = plane1 + width;
                        packedOffset += width * 3;

                        uint previous = 0;
                        for (int x = 0; x < width; x++)
                        {
                            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(source + x * 4, 4));
                            uint pixel24 = FloatBitsToFloat24(bits);
                            uint diff = pixel24 - previous;
                            previous = pixel24;
                            packed[plane0 + x] = (byte)(diff >> 16);
                            packed[plane1 + x] = (byte)(diff >> 8);
                            packed[plane2 + x] = (byte)diff;
                        }
                    }
                }
            }

            ResultCode result = TryCompressZlib(packed, out payload);
            if (result == ResultCode.Success && payload.Length >= raw.Length)
            {
                payload = raw.ToArray();
            }

            return result;
        }

        private static ResultCode TryDecompressPxr24(ChannelLayout[] layouts, int pixelStride, int width, int height, ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            int packedSize = 0;
            for (int i = 0; i < layouts.Length; i++)
            {
                packedSize += width * height * (layouts[i].Type == ExrPixelType.Float ? 3 : layouts[i].SampleSize);
            }

            ResultCode zlibResult = TryDecompressZlib(payload, packedSize, out byte[] packed);
            if (zlibResult != ResultCode.Success)
            {
                raw = Array.Empty<byte>();
                return zlibResult;
            }

            raw = new byte[expectedSize];
            int packedOffset = 0;
            for (int line = 0; line < height; line++)
            {
                int rowBase = line * width * pixelStride;
                for (int channelIndex = 0; channelIndex < layouts.Length; channelIndex++)
                {
                    ChannelLayout layout = layouts[channelIndex];
                    int destination = rowBase + layout.OffsetBytes * width;
                    if (layout.Type == ExrPixelType.UInt)
                    {
                        int plane0 = packedOffset;
                        int plane1 = plane0 + width;
                        int plane2 = plane1 + width;
                        int plane3 = plane2 + width;
                        packedOffset += width * 4;

                        uint pixel = 0;
                        for (int x = 0; x < width; x++)
                        {
                            uint diff = ((uint)packed[plane0 + x] << 24) |
                                        ((uint)packed[plane1 + x] << 16) |
                                        ((uint)packed[plane2 + x] << 8) |
                                        packed[plane3 + x];
                            pixel += diff;
                            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(destination + x * 4, 4), pixel);
                        }
                    }
                    else if (layout.Type == ExrPixelType.Half)
                    {
                        int plane0 = packedOffset;
                        int plane1 = plane0 + width;
                        packedOffset += width * 2;

                        uint pixel = 0;
                        for (int x = 0; x < width; x++)
                        {
                            uint diff = ((uint)packed[plane0 + x] << 8) | packed[plane1 + x];
                            pixel += diff;
                            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(destination + x * 2, 2), (ushort)pixel);
                        }
                    }
                    else
                    {
                        int plane0 = packedOffset;
                        int plane1 = plane0 + width;
                        int plane2 = plane1 + width;
                        packedOffset += width * 3;

                        uint pixel = 0;
                        for (int x = 0; x < width; x++)
                        {
                            uint diff = ((uint)packed[plane0 + x] << 24) |
                                        ((uint)packed[plane1 + x] << 16) |
                                        ((uint)packed[plane2 + x] << 8);
                            pixel += diff;
                            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(destination + x * 4, 4), pixel);
                        }
                    }
                }
            }

            return ResultCode.Success;
        }

        private static ResultCode TryCompressB44(ChannelLayout[] layouts, int pixelStride, int width, int height, byte[] raw, bool isB44A, out byte[] payload)
        {
            EnsureB44Tables();

            using MemoryStream output = new MemoryStream();
            for (int channelIndex = 0; channelIndex < layouts.Length; channelIndex++)
            {
                ChannelLayout layout = layouts[channelIndex];
                if (layout.Type != ExrPixelType.Half)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int sourceOffset = y * width * pixelStride + layout.OffsetBytes * width;
                        output.Write(raw, sourceOffset, width * layout.SampleSize);
                    }

                    continue;
                }

                byte[] blockBytes = new byte[14];
                ushort[] block = new ushort[16];
                for (int by = 0; by < (height + 3) / 4; by++)
                {
                    for (int bx = 0; bx < (width + 3) / 4; bx++)
                    {
                        for (int dy = 0; dy < 4; dy++)
                        {
                            int sourceY = Math.Min(by * 4 + dy, height - 1);
                            int rowBase = sourceY * width * pixelStride + layout.OffsetBytes * width;
                            for (int dx = 0; dx < 4; dx++)
                            {
                                int sourceX = Math.Min(bx * 4 + dx, width - 1);
                                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(rowBase + sourceX * 2, 2));
                                block[dy * 4 + dx] = layout.Linear != 0 ? B44ConvertFromLinear(value) : value;
                            }
                        }

                        int written = PackB44Block(blockBytes, block, isB44A, exactMax: true);
                        output.Write(blockBytes, 0, written);
                    }
                }
            }

            payload = output.ToArray();
            return ResultCode.Success;
        }

        private static ResultCode TryDecompressB44(ChannelLayout[] layouts, int pixelStride, int width, int height, ReadOnlySpan<byte> payload, int expectedSize, out byte[] raw)
        {
            EnsureB44Tables();
            raw = new byte[expectedSize];

            int sourceOffset = 0;
            ushort[] block = new ushort[16];
            for (int channelIndex = 0; channelIndex < layouts.Length; channelIndex++)
            {
                ChannelLayout layout = layouts[channelIndex];
                if (layout.Type != ExrPixelType.Half)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int destination = y * width * pixelStride + layout.OffsetBytes * width;
                        int rowBytes = width * layout.SampleSize;
                        if (sourceOffset + rowBytes > payload.Length)
                        {
                            raw = Array.Empty<byte>();
                            return ResultCode.InvalidData;
                        }

                        payload.Slice(sourceOffset, rowBytes).CopyTo(raw.AsSpan(destination, rowBytes));
                        sourceOffset += rowBytes;
                    }

                    continue;
                }

                for (int by = 0; by < (height + 3) / 4; by++)
                {
                    for (int bx = 0; bx < (width + 3) / 4; bx++)
                    {
                        if (sourceOffset + 3 > payload.Length)
                        {
                            raw = Array.Empty<byte>();
                            return ResultCode.InvalidData;
                        }

                        if (payload[sourceOffset + 2] >= (13 << 2))
                        {
                            UnpackB44FlatBlock(payload.Slice(sourceOffset, 3), block);
                            sourceOffset += 3;
                        }
                        else
                        {
                            if (sourceOffset + 14 > payload.Length)
                            {
                                raw = Array.Empty<byte>();
                                return ResultCode.InvalidData;
                            }

                            UnpackB44Block(payload.Slice(sourceOffset, 14), block);
                            sourceOffset += 14;
                        }

                        if (layout.Linear != 0)
                        {
                            for (int i = 0; i < block.Length; i++)
                            {
                                block[i] = B44ConvertToLinear(block[i]);
                            }
                        }

                        for (int dy = 0; dy < 4; dy++)
                        {
                            int destinationY = by * 4 + dy;
                            if (destinationY >= height)
                            {
                                continue;
                            }

                            int rowBase = destinationY * width * pixelStride + layout.OffsetBytes * width;
                            for (int dx = 0; dx < 4; dx++)
                            {
                                int destinationX = bx * 4 + dx;
                                if (destinationX >= width)
                                {
                                    continue;
                                }

                                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(rowBase + destinationX * 2, 2), block[dy * 4 + dx]);
                            }
                        }
                    }
                }
            }

            if (sourceOffset != payload.Length)
            {
                raw = Array.Empty<byte>();
                return ResultCode.InvalidData;
            }

            return ResultCode.Success;
        }

        private readonly struct PizChannelData
        {
            public PizChannelData(int start, int nx, int ny, int size)
            {
                Start = start;
                Nx = nx;
                Ny = ny;
                Size = size;
            }

            public int Start { get; }

            public int Nx { get; }

            public int Ny { get; }

            public int Size { get; }
        }

        private static void Wenc14(ushort a, ushort b, out ushort l, out ushort h)
        {
            short @as = unchecked((short)a);
            short bs = unchecked((short)b);
            short ms = unchecked((short)((@as + bs) >> 1));
            short ds = unchecked((short)(@as - bs));
            l = unchecked((ushort)ms);
            h = unchecked((ushort)ds);
        }

        private static void Wdec14(ushort l, ushort h, out ushort a, out ushort b)
        {
            short ls = unchecked((short)l);
            short hs = unchecked((short)h);
            int hi = hs;
            int ai = ls + (hi & 1) + (hi >> 1);
            short @as = unchecked((short)ai);
            short bs = unchecked((short)(ai - hi));
            a = unchecked((ushort)@as);
            b = unchecked((ushort)bs);
        }

        private static void Wenc16(ushort a, ushort b, out ushort l, out ushort h)
        {
            const int aOffset = 1 << 15;
            const int mOffset = 1 << 15;
            const int modMask = (1 << 16) - 1;
            int ao = (a + aOffset) & modMask;
            int m = (ao + b) >> 1;
            int d = ao - b;
            if (d < 0)
            {
                m = (m + mOffset) & modMask;
            }

            d &= modMask;
            l = (ushort)m;
            h = (ushort)d;
        }

        private static void Wdec16(ushort l, ushort h, out ushort a, out ushort b)
        {
            const int aOffset = 1 << 15;
            const int modMask = (1 << 16) - 1;
            int m = l;
            int d = h;
            int bb = (m - (d >> 1)) & modMask;
            int aa = (d + bb - aOffset) & modMask;
            b = (ushort)bb;
            a = (ushort)aa;
        }

        private static void Wav2Encode(ushort[] buffer, int start, int nx, int ox, int ny, int oy, ushort maxValue)
        {
            bool w14 = maxValue < (1 << 14);
            int n = Math.Min(nx, ny);
            int p = 1;
            int p2 = 2;
            while (p2 <= n)
            {
                int oy1 = oy * p;
                int oy2 = oy * p2;
                int ox1 = ox * p;
                int ox2 = ox * p2;

                for (int py = start; py <= start + oy * (ny - p2); py += oy2)
                {
                    int px = py;
                    int ex = py + ox * (nx - p2);
                    for (; px <= ex; px += ox2)
                    {
                        int p01 = px + ox1;
                        int p10 = px + oy1;
                        int p11 = p10 + ox1;
                        ushort i00;
                        ushort i01;
                        ushort i10;
                        ushort i11;
                        if (w14)
                        {
                            Wenc14(buffer[px], buffer[p01], out i00, out i01);
                            Wenc14(buffer[p10], buffer[p11], out i10, out i11);
                            Wenc14(i00, i10, out buffer[px], out buffer[p10]);
                            Wenc14(i01, i11, out buffer[p01], out buffer[p11]);
                        }
                        else
                        {
                            Wenc16(buffer[px], buffer[p01], out i00, out i01);
                            Wenc16(buffer[p10], buffer[p11], out i10, out i11);
                            Wenc16(i00, i10, out buffer[px], out buffer[p10]);
                            Wenc16(i01, i11, out buffer[p01], out buffer[p11]);
                        }
                    }

                    if ((nx & p) != 0)
                    {
                        int p10 = px + oy1;
                        ushort i00;
                        if (w14)
                        {
                            Wenc14(buffer[px], buffer[p10], out i00, out buffer[p10]);
                        }
                        else
                        {
                            Wenc16(buffer[px], buffer[p10], out i00, out buffer[p10]);
                        }

                        buffer[px] = i00;
                    }
                }

                if ((ny & p) != 0)
                {
                    int py = start + oy * (ny - p);
                    int ex = py + ox * (nx - p2);
                    for (int px = py; px <= ex; px += ox2)
                    {
                        int p01 = px + ox1;
                        ushort i00;
                        if (w14)
                        {
                            Wenc14(buffer[px], buffer[p01], out i00, out buffer[p01]);
                        }
                        else
                        {
                            Wenc16(buffer[px], buffer[p01], out i00, out buffer[p01]);
                        }

                        buffer[px] = i00;
                    }
                }

                p = p2;
                p2 <<= 1;
            }
        }

        private static void Wav2Decode(ushort[] buffer, int start, int nx, int ox, int ny, int oy, ushort maxValue)
        {
            bool w14 = maxValue < (1 << 14);
            int n = Math.Min(nx, ny);
            int p = 1;
            while (p <= n)
            {
                p <<= 1;
            }

            int p2 = p >> 1;
            p = p2 >> 1;

            while (p >= 1)
            {
                int oy1 = oy * p;
                int oy2 = oy * p2;
                int ox1 = ox * p;
                int ox2 = ox * p2;

                for (int py = start; py <= start + oy * (ny - p2); py += oy2)
                {
                    int px = py;
                    int ex = py + ox * (nx - p2);
                    for (; px <= ex; px += ox2)
                    {
                        int p01 = px + ox1;
                        int p10 = px + oy1;
                        int p11 = p10 + ox1;
                        ushort i00;
                        ushort i01;
                        ushort i10;
                        ushort i11;
                        if (w14)
                        {
                            Wdec14(buffer[px], buffer[p10], out i00, out i10);
                            Wdec14(buffer[p01], buffer[p11], out i01, out i11);
                            Wdec14(i00, i01, out buffer[px], out buffer[p01]);
                            Wdec14(i10, i11, out buffer[p10], out buffer[p11]);
                        }
                        else
                        {
                            Wdec16(buffer[px], buffer[p10], out i00, out i10);
                            Wdec16(buffer[p01], buffer[p11], out i01, out i11);
                            Wdec16(i00, i01, out buffer[px], out buffer[p01]);
                            Wdec16(i10, i11, out buffer[p10], out buffer[p11]);
                        }
                    }

                    if ((nx & p) != 0)
                    {
                        int p10 = px + oy1;
                        ushort i00;
                        if (w14)
                        {
                            Wdec14(buffer[px], buffer[p10], out i00, out buffer[p10]);
                        }
                        else
                        {
                            Wdec16(buffer[px], buffer[p10], out i00, out buffer[p10]);
                        }

                        buffer[px] = i00;
                    }
                }

                if ((ny & p) != 0)
                {
                    int py = start + oy * (ny - p);
                    int ex = py + ox * (nx - p2);
                    for (int px = py; px <= ex; px += ox2)
                    {
                        int p01 = px + ox1;
                        ushort i00;
                        if (w14)
                        {
                            Wdec14(buffer[px], buffer[p01], out i00, out buffer[p01]);
                        }
                        else
                        {
                            Wdec16(buffer[px], buffer[p01], out i00, out buffer[p01]);
                        }

                        buffer[px] = i00;
                    }
                }

                p2 = p;
                p >>= 1;
            }
        }

        private static void BitmapFromData(ushort[] data, byte[] bitmap, out ushort minNonZero, out ushort maxNonZero)
        {
            Array.Clear(bitmap, 0, bitmap.Length);
            for (int i = 0; i < data.Length; i++)
            {
                bitmap[data[i] >> 3] |= (byte)(1 << (data[i] & 7));
            }

            bitmap[0] &= 0xfe;
            minNonZero = BitmapSize - 1;
            maxNonZero = 0;
            for (ushort i = 0; i < bitmap.Length; i++)
            {
                if (bitmap[i] != 0)
                {
                    if (minNonZero > i)
                    {
                        minNonZero = i;
                    }

                    if (maxNonZero < i)
                    {
                        maxNonZero = i;
                    }
                }
            }
        }

        private static ushort ForwardLutFromBitmap(byte[] bitmap, ushort[] lut)
        {
            int k = 0;
            for (int i = 0; i < UShortRange; i++)
            {
                if (i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0)
                {
                    lut[i] = (ushort)k++;
                }
                else
                {
                    lut[i] = 0;
                }
            }

            return (ushort)(k - 1);
        }

        private static ushort ReverseLutFromBitmap(byte[] bitmap, ushort[] lut)
        {
            int k = 0;
            for (int i = 0; i < UShortRange; i++)
            {
                if (i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0)
                {
                    lut[k++] = (ushort)i;
                }
            }

            int n = k - 1;
            while (k < UShortRange)
            {
                lut[k++] = 0;
            }

            return (ushort)n;
        }

        private static void ApplyLut(ushort[] lut, ushort[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = lut[data[i]];
            }
        }

        private static int HufLength(long code) => (int)(code & 63);

        private static long HufCode(long code) => code >> 6;

        private static void HufCanonicalCodeTable(long[] codes)
        {
            long[] counts = new long[59];
            for (int i = 0; i < codes.Length; i++)
            {
                counts[codes[i]]++;
            }

            long code = 0;
            for (int i = 58; i > 0; i--)
            {
                long nextCode = (code + counts[i]) >> 1;
                counts[i] = code;
                code = nextCode;
            }

            for (int i = 0; i < codes.Length; i++)
            {
                int length = (int)codes[i];
                if (length > 0)
                {
                    codes[i] = (long)length | (counts[length]++ << 6);
                }
            }
        }

        private static bool HeapLess(int left, int right, long[] freq)
        {
            long leftFreq = freq[left];
            long rightFreq = freq[right];
            return leftFreq < rightFreq || (leftFreq == rightFreq && left < right);
        }

        private static void HeapPush(int[] heap, ref int count, int value, long[] freq)
        {
            int index = count++;
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (!HeapLess(value, heap[parent], freq))
                {
                    break;
                }

                heap[index] = heap[parent];
                index = parent;
            }

            heap[index] = value;
        }

        private static int HeapPop(int[] heap, ref int count, long[] freq)
        {
            int result = heap[0];
            int value = heap[--count];
            int index = 0;
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= count)
                {
                    break;
                }

                int right = left + 1;
                int child = right < count && HeapLess(heap[right], heap[left], freq) ? right : left;
                if (!HeapLess(heap[child], value, freq))
                {
                    break;
                }

                heap[index] = heap[child];
                index = child;
            }

            if (count > 0)
            {
                heap[index] = value;
            }

            return result;
        }

        private static bool TryHufBuildEncTable(long[] freq, out long[] codes, out int im, out int iM)
        {
            codes = new long[HufEncSize];
            int[] heap = new int[HufEncSize];
            int[] hlink = new int[HufEncSize];
            im = 0;
            while (im < HufEncSize && freq[im] == 0)
            {
                im++;
            }

            if (im >= HufEncSize)
            {
                iM = 0;
                return false;
            }

            int heapCount = 0;
            iM = im;
            for (int i = im; i < HufEncSize; i++)
            {
                hlink[i] = i;
                if (freq[i] != 0)
                {
                    HeapPush(heap, ref heapCount, i, freq);
                    iM = i;
                }
            }

            iM++;
            freq[iM] = 1;
            hlink[iM] = iM;
            HeapPush(heap, ref heapCount, iM, freq);

            while (heapCount > 1)
            {
                int mm = HeapPop(heap, ref heapCount, freq);
                int m = HeapPop(heap, ref heapCount, freq);
                freq[m] += freq[mm];
                HeapPush(heap, ref heapCount, m, freq);

                for (int j = m; ; j = hlink[j])
                {
                    codes[j]++;
                    if (codes[j] > 58)
                    {
                        return false;
                    }

                    if (hlink[j] == j)
                    {
                        hlink[j] = mm;
                        break;
                    }
                }

                for (int j = mm; ; j = hlink[j])
                {
                    codes[j]++;
                    if (codes[j] > 58)
                    {
                        return false;
                    }

                    if (hlink[j] == j)
                    {
                        break;
                    }
                }
            }

            HufCanonicalCodeTable(codes);
            return true;
        }

        private static void HufPackEncTable(long[] codes, int im, int iM, BitWriter writer)
        {
            for (; im <= iM; im++)
            {
                int length = HufLength(codes[im]);
                if (length == 0)
                {
                    int zeroRun = 1;
                    while (im < iM && zeroRun < LongestLongRun)
                    {
                        if (HufLength(codes[im + 1]) > 0)
                        {
                            break;
                        }

                        im++;
                        zeroRun++;
                    }

                    if (zeroRun >= 2)
                    {
                        if (zeroRun >= ShortestLongRun)
                        {
                            writer.WriteBits(6, LongZeroCodeRun);
                            writer.WriteBits(8, (ulong)(zeroRun - ShortestLongRun));
                        }
                        else
                        {
                            writer.WriteBits(6, (ulong)(ShortZeroCodeRun + zeroRun - 2));
                        }

                        continue;
                    }
                }

                writer.WriteBits(6, (ulong)length);
            }
        }

        private static bool TryHufUnpackEncTable(byte[] bytes, int offset, int length, int im, int iM, out long[] codes)
        {
            codes = new long[HufEncSize];
            BitReader reader = new BitReader(bytes, offset, length);
            for (int index = im; index <= iM; index++)
            {
                if (!reader.TryReadBits(6, out int readLength))
                {
                    return false;
                }

                codes[index] = readLength;
                if (readLength == LongZeroCodeRun)
                {
                    if (!reader.TryReadBits(8, out int zeroRunDelta))
                    {
                        return false;
                    }

                    int zeroRun = zeroRunDelta + ShortestLongRun;
                    if (index + zeroRun > iM + 1)
                    {
                        return false;
                    }

                    while (zeroRun-- > 0)
                    {
                        codes[index++] = 0;
                    }

                    index--;
                }
                else if (readLength >= ShortZeroCodeRun)
                {
                    int zeroRun = readLength - ShortZeroCodeRun + 2;
                    if (index + zeroRun > iM + 1)
                    {
                        return false;
                    }

                    while (zeroRun-- > 0)
                    {
                        codes[index++] = 0;
                    }

                    index--;
                }
            }

            HufCanonicalCodeTable(codes);
            return true;
        }

        private static bool TryHufBuildDecTable(long[] codes, int im, int iM, out HufDecEntry[] table)
        {
            table = new HufDecEntry[HufDecSize];
            List<int>[] longSymbols = new List<int>[HufDecSize];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = new HufDecEntry();
            }

            for (int index = im; index <= iM; index++)
            {
                long code = HufCode(codes[index]);
                int length = HufLength(codes[index]);
                if ((code >> length) != 0)
                {
                    return false;
                }

                if (length > HufDecBits)
                {
                    int slot = (int)(code >> (length - HufDecBits));
                    if (table[slot].Length != 0)
                    {
                        return false;
                    }

                    (longSymbols[slot] ??= new List<int>()).Add(index);
                }
                else if (length != 0)
                {
                    int slot = (int)(code << (HufDecBits - length));
                    int count = 1 << (HufDecBits - length);
                    for (int i = 0; i < count; i++)
                    {
                        if (table[slot + i].Length != 0 || longSymbols[slot + i] != null)
                        {
                            return false;
                        }

                        table[slot + i].Length = (byte)length;
                        table[slot + i].Literal = index;
                    }
                }
            }

            for (int i = 0; i < longSymbols.Length; i++)
            {
                if (longSymbols[i] != null)
                {
                    table[i].Literal = longSymbols[i]!.Count;
                    table[i].Symbols = longSymbols[i]!.ToArray();
                }
            }

            return true;
        }

        private static void OutputCode(long code, BitWriter writer)
        {
            writer.WriteBits(HufLength(code), (ulong)HufCode(code));
        }

        private static void SendCode(long symbolCode, int runCount, long runCode, BitWriter writer)
        {
            if (HufLength(symbolCode) + HufLength(runCode) + 8 < HufLength(symbolCode) * runCount)
            {
                OutputCode(symbolCode, writer);
                OutputCode(runCode, writer);
                writer.WriteBits(8, (ulong)runCount);
                return;
            }

            while (runCount-- >= 0)
            {
                OutputCode(symbolCode, writer);
            }
        }

        private static byte[] HufCompress(ushort[] raw)
        {
            if (raw.Length == 0)
            {
                return Array.Empty<byte>();
            }

            long[] freq = new long[HufEncSize];
            for (int i = 0; i < raw.Length; i++)
            {
                freq[raw[i]]++;
            }

            if (!TryHufBuildEncTable(freq, out long[] codes, out int im, out int iM))
            {
                throw new InvalidOperationException("Failed to build Huffman encoding table for PIZ data.");
            }

            BitWriter tableWriter = new BitWriter();
            HufPackEncTable(codes, im, iM, tableWriter);
            byte[] tableBytes = tableWriter.ToArray();

            BitWriter dataWriter = new BitWriter();
            int symbol = raw[0];
            int count = 0;
            for (int i = 1; i < raw.Length; i++)
            {
                if (symbol == raw[i] && count < byte.MaxValue)
                {
                    count++;
                }
                else
                {
                    SendCode(codes[symbol], count, codes[iM], dataWriter);
                    count = 0;
                }

                symbol = raw[i];
            }

            SendCode(codes[symbol], count, codes[iM], dataWriter);
            byte[] dataBytes = dataWriter.ToArray();

            byte[] output = new byte[20 + tableBytes.Length + dataBytes.Length];
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0, 4), im);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4, 4), iM);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8, 4), tableBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(12, 4), dataWriter.TotalBits);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16, 4), 0);
            tableBytes.CopyTo(output, 20);
            dataBytes.CopyTo(output, 20 + tableBytes.Length);
            return output;
        }

        private static bool TryHufUncompress(byte[] compressed, int expectedCount, out ushort[] raw)
        {
            raw = new ushort[expectedCount];
            if (compressed.Length == 0)
            {
                return false;
            }

            if (compressed.Length < 20)
            {
                return false;
            }

            int im = BinaryPrimitives.ReadInt32LittleEndian(compressed.AsSpan(0, 4));
            int iM = BinaryPrimitives.ReadInt32LittleEndian(compressed.AsSpan(4, 4));
            int tableLength = BinaryPrimitives.ReadInt32LittleEndian(compressed.AsSpan(8, 4));
            int nBits = BinaryPrimitives.ReadInt32LittleEndian(compressed.AsSpan(12, 4));
            if (im < 0 || im >= HufEncSize || iM < 0 || iM >= HufEncSize || tableLength < 0)
            {
                return false;
            }

            int tableOffset = 20;
            int dataOffset = tableOffset + tableLength;
            if (dataOffset > compressed.Length || nBits > (compressed.Length - dataOffset) * 8)
            {
                return false;
            }

            if (!TryHufUnpackEncTable(compressed, tableOffset, tableLength, im, iM, out long[] codes))
            {
                return false;
            }

            if (!TryHufBuildDecTable(codes, im, iM, out HufDecEntry[] table))
            {
                return false;
            }

            return TryHufDecode(codes, table, compressed, dataOffset, nBits, iM, raw);
        }

        private static bool TryHufDecode(long[] codes, HufDecEntry[] table, byte[] input, int dataOffset, int bitLength, int runLengthCode, ushort[] output)
        {
            long buffer = 0;
            int bitCount = 0;
            int inOffset = dataOffset;
            int inputEnd = dataOffset + ((bitLength + 7) / 8);
            int outOffset = 0;

            while (inOffset < inputEnd)
            {
                buffer = (buffer << 8) | input[inOffset++];
                bitCount += 8;

                while (bitCount >= HufDecBits)
                {
                    HufDecEntry entry = table[(int)((buffer >> (bitCount - HufDecBits)) & HufDecMask)];
                    if (entry.Length != 0)
                    {
                        bitCount -= entry.Length;
                        if (!TryGetCode(entry.Literal, runLengthCode, ref buffer, ref bitCount, input, ref inOffset, inputEnd, output, ref outOffset))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (entry.Symbols == null)
                        {
                            return false;
                        }

                        bool matched = false;
                        for (int i = 0; i < entry.Symbols.Length; i++)
                        {
                            int symbol = entry.Symbols[i];
                            int length = HufLength(codes[symbol]);
                            while (bitCount < length && inOffset < inputEnd)
                            {
                                buffer = (buffer << 8) | input[inOffset++];
                                bitCount += 8;
                            }

                            if (bitCount >= length &&
                                HufCode(codes[symbol]) == ((buffer >> (bitCount - length)) & (((long)1 << length) - 1)))
                            {
                                bitCount -= length;
                                if (!TryGetCode(symbol, runLengthCode, ref buffer, ref bitCount, input, ref inOffset, inputEnd, output, ref outOffset))
                                {
                                    return false;
                                }

                                matched = true;
                                break;
                            }
                        }

                        if (!matched)
                        {
                            return false;
                        }
                    }
                }
            }

            int shift = (8 - bitLength) & 7;
            buffer >>= shift;
            bitCount -= shift;

            while (bitCount > 0)
            {
                HufDecEntry entry = table[(int)((buffer << (HufDecBits - bitCount)) & HufDecMask)];
                if (entry.Length == 0)
                {
                    return false;
                }

                bitCount -= entry.Length;
                if (!TryGetCode(entry.Literal, runLengthCode, ref buffer, ref bitCount, input, ref inOffset, inputEnd, output, ref outOffset))
                {
                    return false;
                }
            }

            return outOffset == output.Length;
        }

        private static bool TryGetCode(int symbol, int runLengthCode, ref long buffer, ref int bitCount, byte[] input, ref int inOffset, int inputEnd, ushort[] output, ref int outOffset)
        {
            if (symbol == runLengthCode)
            {
                if (bitCount < 8)
                {
                    if (inOffset >= inputEnd)
                    {
                        return false;
                    }

                    buffer = (buffer << 8) | input[inOffset++];
                    bitCount += 8;
                }

                bitCount -= 8;
                int count = (int)((buffer >> bitCount) & 0xff);
                if (outOffset == 0 || outOffset + count > output.Length)
                {
                    return false;
                }

                ushort value = output[outOffset - 1];
                while (count-- > 0)
                {
                    output[outOffset++] = value;
                }

                return true;
            }

            if (outOffset >= output.Length)
            {
                return false;
            }

            output[outOffset++] = (ushort)symbol;
            return true;
        }

        private static uint FloatBitsToFloat24(uint bits)
        {
            uint sign = bits & 0x80000000u;
            uint exponent = bits & 0x7f800000u;
            uint mantissa = bits & 0x007fffffu;
            if (exponent == 0x7f800000u)
            {
                if (mantissa != 0)
                {
                    mantissa >>= 8;
                    return (sign >> 8) | (exponent >> 8) | mantissa | (mantissa == 0 ? 1u : 0u);
                }

                return (sign >> 8) | (exponent >> 8);
            }

            uint value = ((exponent | mantissa) + (mantissa & 0x00000080u)) >> 8;
            if (value >= 0x7f8000u)
            {
                value = (exponent | mantissa) >> 8;
            }

            return (sign >> 8) | value;
        }

        private static void EnsureB44Tables()
        {
            if (s_b44TablesInitialized)
            {
                return;
            }

            lock (B44TableLock)
            {
                if (s_b44TablesInitialized)
                {
                    return;
                }

                for (int i = 0; i < UShortRange; i++)
                {
                    ushort value = (ushort)i;
                    if ((value & 0x7c00) == 0x7c00)
                    {
                        B44ExpTable[i] = 0;
                        B44LogTable[i] = 0;
                        continue;
                    }

                    if (value >= 0x558c && value < 0x8000)
                    {
                        B44ExpTable[i] = 0x7bff;
                    }
                    else
                    {
                        float f = HalfHelper.HalfToSingle(value);
                        B44ExpTable[i] = HalfHelper.SingleToHalf((float)Math.Exp(f / 8.0f));
                    }

                    if (value > 0x8000)
                    {
                        B44LogTable[i] = 0;
                        continue;
                    }

                    float logInput = HalfHelper.HalfToSingle(value);
                    B44LogTable[i] = logInput <= 0.0f
                        ? (ushort)0
                        : HalfHelper.SingleToHalf((float)(8.0 * Math.Log(logInput)));
                }

                s_b44TablesInitialized = true;
            }
        }

        private static ushort B44ConvertFromLinear(ushort value) => B44ExpTable[value];

        private static ushort B44ConvertToLinear(ushort value) => B44LogTable[value];

        private static int B44ShiftAndRound(int value, int shift)
        {
            value <<= 1;
            int a = (1 << shift) - 1;
            shift++;
            int b = (value >> shift) & 1;
            return (value + a + b) >> shift;
        }

        private static int PackB44Block(Span<byte> output, ushort[] block, bool flatFields, bool exactMax)
        {
            int[] deltas = new int[16];
            int[] runs = new int[15];
            ushort[] ordered = new ushort[16];
            ushort max = 0;
            int shift = -1;
            const int bias = 0x20;

            for (int i = 0; i < 16; i++)
            {
                ushort value = block[i];
                if ((value & 0x7c00) == 0x7c00)
                {
                    ordered[i] = 0x8000;
                }
                else if ((value & 0x8000) != 0)
                {
                    ordered[i] = unchecked((ushort)~value);
                }
                else
                {
                    ordered[i] = (ushort)(value | 0x8000);
                }

                if (ordered[i] > max)
                {
                    max = ordered[i];
                }
            }

            int minRun;
            int maxRun;
            do
            {
                shift++;
                for (int i = 0; i < 16; i++)
                {
                    deltas[i] = B44ShiftAndRound(max - ordered[i], shift);
                }

                runs[0] = deltas[0] - deltas[4] + bias;
                runs[1] = deltas[4] - deltas[8] + bias;
                runs[2] = deltas[8] - deltas[12] + bias;
                runs[3] = deltas[0] - deltas[1] + bias;
                runs[4] = deltas[4] - deltas[5] + bias;
                runs[5] = deltas[8] - deltas[9] + bias;
                runs[6] = deltas[12] - deltas[13] + bias;
                runs[7] = deltas[1] - deltas[2] + bias;
                runs[8] = deltas[5] - deltas[6] + bias;
                runs[9] = deltas[9] - deltas[10] + bias;
                runs[10] = deltas[13] - deltas[14] + bias;
                runs[11] = deltas[2] - deltas[3] + bias;
                runs[12] = deltas[6] - deltas[7] + bias;
                runs[13] = deltas[10] - deltas[11] + bias;
                runs[14] = deltas[14] - deltas[15] + bias;

                minRun = runs[0];
                maxRun = runs[0];
                for (int i = 1; i < runs.Length; i++)
                {
                    if (runs[i] < minRun)
                    {
                        minRun = runs[i];
                    }

                    if (runs[i] > maxRun)
                    {
                        maxRun = runs[i];
                    }
                }
            }
            while (minRun < 0 || maxRun > 0x3f);

            if (minRun == bias && maxRun == bias && flatFields)
            {
                output[0] = (byte)(ordered[0] >> 8);
                output[1] = (byte)ordered[0];
                output[2] = 0xfc;
                return 3;
            }

            if (exactMax)
            {
                ordered[0] = (ushort)(max - (deltas[0] << shift));
            }

            output[0] = (byte)(ordered[0] >> 8);
            output[1] = (byte)ordered[0];
            output[2] = (byte)((shift << 2) | (runs[0] >> 4));
            output[3] = (byte)((runs[0] << 4) | (runs[1] >> 2));
            output[4] = (byte)((runs[1] << 6) | runs[2]);
            output[5] = (byte)((runs[3] << 2) | (runs[4] >> 4));
            output[6] = (byte)((runs[4] << 4) | (runs[5] >> 2));
            output[7] = (byte)((runs[5] << 6) | runs[6]);
            output[8] = (byte)((runs[7] << 2) | (runs[8] >> 4));
            output[9] = (byte)((runs[8] << 4) | (runs[9] >> 2));
            output[10] = (byte)((runs[9] << 6) | runs[10]);
            output[11] = (byte)((runs[11] << 2) | (runs[12] >> 4));
            output[12] = (byte)((runs[12] << 4) | (runs[13] >> 2));
            output[13] = (byte)((runs[13] << 6) | runs[14]);
            return 14;
        }

        private static void UnpackB44Block(ReadOnlySpan<byte> input, ushort[] block)
        {
            ushort s0 = (ushort)((input[0] << 8) | input[1]);
            int shift = input[2] >> 2;
            uint bias = (uint)(0x20u << shift);
            ushort s4 = (ushort)(s0 + ((((uint)input[2] << 4) | ((uint)input[3] >> 4)) & 0x3fu) * (1u << shift) - bias);
            ushort s8 = (ushort)(s4 + ((((uint)input[3] << 2) | ((uint)input[4] >> 6)) & 0x3fu) * (1u << shift) - bias);
            ushort s12 = (ushort)(s8 + ((uint)(input[4] & 0x3f) * (1u << shift)) - bias);
            ushort s1 = (ushort)(s0 + ((uint)(input[5] >> 2) * (1u << shift)) - bias);
            ushort s5 = (ushort)(s4 + ((((uint)input[5] << 4) | ((uint)input[6] >> 4)) & 0x3fu) * (1u << shift) - bias);
            ushort s9 = (ushort)(s8 + ((((uint)input[6] << 2) | ((uint)input[7] >> 6)) & 0x3fu) * (1u << shift) - bias);
            ushort s13 = (ushort)(s12 + ((uint)(input[7] & 0x3f) * (1u << shift)) - bias);
            ushort s2 = (ushort)(s1 + ((uint)(input[8] >> 2) * (1u << shift)) - bias);
            ushort s6 = (ushort)(s5 + ((((uint)input[8] << 4) | ((uint)input[9] >> 4)) & 0x3fu) * (1u << shift) - bias);
            ushort s10 = (ushort)(s9 + ((((uint)input[9] << 2) | ((uint)input[10] >> 6)) & 0x3fu) * (1u << shift) - bias);
            ushort s14 = (ushort)(s13 + ((uint)(input[10] & 0x3f) * (1u << shift)) - bias);
            ushort s3 = (ushort)(s2 + ((uint)(input[11] >> 2) * (1u << shift)) - bias);
            ushort s7 = (ushort)(s6 + ((((uint)input[11] << 4) | ((uint)input[12] >> 4)) & 0x3fu) * (1u << shift) - bias);
            ushort s11 = (ushort)(s10 + ((((uint)input[12] << 2) | ((uint)input[13] >> 6)) & 0x3fu) * (1u << shift) - bias);
            ushort s15 = (ushort)(s14 + ((uint)(input[13] & 0x3f) * (1u << shift)) - bias);

            block[0] = s0;
            block[1] = s1;
            block[2] = s2;
            block[3] = s3;
            block[4] = s4;
            block[5] = s5;
            block[6] = s6;
            block[7] = s7;
            block[8] = s8;
            block[9] = s9;
            block[10] = s10;
            block[11] = s11;
            block[12] = s12;
            block[13] = s13;
            block[14] = s14;
            block[15] = s15;

            for (int i = 0; i < block.Length; i++)
            {
                block[i] = (block[i] & 0x8000) != 0
                    ? (ushort)(block[i] & 0x7fff)
                    : unchecked((ushort)~block[i]);
            }
        }

        private static void UnpackB44FlatBlock(ReadOnlySpan<byte> input, ushort[] block)
        {
            ushort ordered = (ushort)((input[0] << 8) | input[1]);
            ushort value = (ordered & 0x8000) != 0
                ? (ushort)(ordered & 0x7fff)
                : unchecked((ushort)~ordered);
            for (int i = 0; i < block.Length; i++)
            {
                block[i] = value;
            }
        }
    }
}
