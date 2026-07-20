using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using TinyEXR.PortV1;
using TinyEXR.V3.Format;

namespace TinyEXR.V3
{
    internal sealed class WriterFilePlan
    {
        public WriterFilePlan(byte[] prefix, bool multipart, WriterPartData[] parts)
        {
            Prefix = prefix;
            Multipart = multipart;
            Parts = parts;
        }

        public byte[] Prefix { get; }

        public bool Multipart { get; }

        public WriterPartData[] Parts { get; }
    }

    internal sealed class WriterPartData
    {
        public WriterPartData(
            int partIndex,
            Header header,
            ReaderPartLayout layout,
            long offsetTablePosition,
            long? maximumSamplesPosition)
        {
            PartIndex = partIndex;
            Header = header;
            Layout = layout;
            OffsetTablePosition = offsetTablePosition;
            MaximumSamplesPosition = maximumSamplesPosition;
            Offsets = new ulong[layout.BlockCount];
            Written = new bool[layout.BlockCount];

            CodecChannels = new List<ExrChannel>(header.Channels.Count);
            for (int i = 0; i < header.Channels.Count; i++)
            {
                Channel channel = header.Channels[i];
                CodecChannels.Add(new ExrChannel(
                    channel.Name,
                    (ExrPixelType)(int)channel.PixelType,
                    channel.XSampling,
                    channel.YSampling,
                    channel.PerceptuallyLinear ? (byte)1 : (byte)0));
            }
        }

        public int PartIndex { get; }

        public Header Header { get; }

        public ReaderPartLayout Layout { get; }

        public long OffsetTablePosition { get; }

        public long? MaximumSamplesPosition { get; }

        public ulong[] Offsets { get; }

        public bool[] Written { get; }

        public List<ExrChannel> CodecChannels { get; }

        public int MaximumSamplesPerPixel { get; set; }
    }

    internal sealed class WriterPlanException : Exception
    {
        public WriterPlanException(ExrResult result, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Result = result;
        }

        public ExrResult Result { get; }
    }

    internal static class WriterPlanBuilder
    {
        private const uint TiledFlag = 1U << 9;
        private const uint LongNamesFlag = 1U << 10;
        private const uint NonImageFlag = 1U << 11;
        private const uint MultipartFlag = 1U << 12;
        private const int ShortNameByteLimit = 31;
        private const int WindowCoordinateLimit = int.MaxValue / 2;

        private static readonly HashSet<string> ReservedAttributeNames = new HashSet<string>(StringComparer.Ordinal)
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
            "version",
            "maxSamplesPerPixel",
            "chromaticities",
        };

        public static WriterFilePlan Build(
            IReadOnlyList<Header> headers,
            WriterLimits limits,
            bool forceMultipart)
        {
            if (headers.Count == 0)
            {
                throw Invalid("At least one part must be added before Begin.");
            }

            Limit(nameof(limits.MaximumParts), headers.Count, limits.MaximumParts);
            bool multipart = forceMultipart || headers.Count > 1;
            bool anyTiled = false;
            bool anyDeep = false;
            bool longNames = false;
            long totalBlocks = 0;
            long offsetTableBytes = 0;
            int[] blockCounts = new int[headers.Count];
            HashSet<string>? partNames = multipart
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;

            for (int partIndex = 0; partIndex < headers.Count; partIndex++)
            {
                Header header = headers[partIndex] ?? throw Invalid("The part collection contains a null header.");
                ValidateHeader(header, multipart, partNames, limits, ref longNames);
                anyTiled |= header.IsTiled;
                anyDeep |= header.IsDeep;

                int blockCount;
                try
                {
                    blockCount = ExrFormatParser.ComputeChunkCount(header);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
                {
                    throw Invalid("The part geometry cannot be represented by an EXR chunk table.", exception);
                }

                Limit(nameof(limits.MaximumBlocksPerPart), blockCount, limits.MaximumBlocksPerPart);
                totalBlocks = checked(totalBlocks + blockCount);
                offsetTableBytes = checked(offsetTableBytes + checked((long)blockCount * sizeof(ulong)));
                blockCounts[partIndex] = blockCount;
            }

            Limit(nameof(limits.MaximumTotalBlocks), totalBlocks, limits.MaximumTotalBlocks);
            Limit(
                nameof(limits.MaximumOffsetTableByteCount),
                offsetTableBytes,
                limits.MaximumOffsetTableByteCount);

            using MemoryStream output = new MemoryStream();
            WriteUInt32(output, ExrFormatParser.Magic);
            uint versionField = ExrFormatParser.SupportedFileVersion;
            if (multipart)
            {
                versionField |= MultipartFlag;
            }
            else if (anyTiled && !anyDeep)
            {
                versionField |= TiledFlag;
            }

            if (anyDeep)
            {
                versionField |= NonImageFlag;
            }

            if (longNames)
            {
                versionField |= LongNamesFlag;
            }

            WriteUInt32(output, versionField);

            long?[] maximumSamplesPositions = new long?[headers.Count];
            for (int partIndex = 0; partIndex < headers.Count; partIndex++)
            {
                WriteHeader(
                    output,
                    headers[partIndex],
                    multipart,
                    blockCounts[partIndex],
                    out maximumSamplesPositions[partIndex]);
            }

            if (multipart)
            {
                output.WriteByte(0);
            }

            Limit(
                nameof(limits.MaximumHeaderByteCount),
                output.Length,
                limits.MaximumHeaderByteCount);

            long[] offsetTablePositions = new long[headers.Count];
            Span<byte> zeroOffset = stackalloc byte[sizeof(ulong)];
            for (int partIndex = 0; partIndex < headers.Count; partIndex++)
            {
                offsetTablePositions[partIndex] = output.Position;
                for (int blockIndex = 0; blockIndex < blockCounts[partIndex]; blockIndex++)
                {
                    output.Write(zeroOffset);
                }
            }

            byte[] prefix = output.ToArray();
            WriterPartData[] parts = new WriterPartData[headers.Count];
            for (int partIndex = 0; partIndex < headers.Count; partIndex++)
            {
                ReaderPartLayout layout = ReaderPartLayout.Create(
                    headers[partIndex],
                    multipart,
                    blockCounts[partIndex]);
                parts[partIndex] = new WriterPartData(
                    partIndex,
                    headers[partIndex],
                    layout,
                    offsetTablePositions[partIndex],
                    maximumSamplesPositions[partIndex]);
            }

            return new WriterFilePlan(prefix, multipart, parts);
        }

        private static void ValidateHeader(
            Header header,
            bool multipart,
            HashSet<string>? partNames,
            WriterLimits limits,
            ref bool longNames)
        {
            Limit(nameof(limits.MaximumChannelsPerPart), header.Channels.Count, limits.MaximumChannelsPerPart);
            Limit(nameof(limits.MaximumAttributesPerPart), header.Attributes.Count, limits.MaximumAttributesPerPart);
            Limit(nameof(limits.MaximumDimension), header.DataWindow.Width, limits.MaximumDimension);
            Limit(nameof(limits.MaximumDimension), header.DataWindow.Height, limits.MaximumDimension);

            ValidateWindow(header.DataWindow);
            ValidateWindow(header.DisplayWindow);
            if (header.Compression == Compression.DWAA || header.Compression == Compression.DWAB)
            {
                throw Unsupported($"Compression '{header.Compression}' is not supported by tinyexr v3.");
            }

            if (header.IsDeep &&
                header.Compression != Compression.None &&
                header.Compression != Compression.RLE &&
                header.Compression != Compression.ZIPS &&
                header.Compression != Compression.ZIP &&
                header.Compression != Compression.HTJ2K256 &&
                header.Compression != Compression.HTJ2K32 &&
                header.Compression != Compression.ZSTD)
            {
                throw Unsupported(
                    $"Compression '{header.Compression}' is not permitted for deep OpenEXR data.");
            }

            if (multipart)
            {
                if (header.Name.Length == 0)
                {
                    throw Invalid("Every multipart part requires a non-empty name.");
                }

                if (!partNames!.Add(header.Name))
                {
                    throw Invalid($"Duplicate multipart part name '{header.Name}'.");
                }

                longNames |= IsLongName(header.Name);
            }

            if (!header.IsTiled && header.LineOrder == LineOrder.RandomY)
            {
                throw Invalid("RandomY line order is valid only for tiled parts.");
            }

            for (int i = 0; i < header.Channels.Count; i++)
            {
                Channel channel = header.Channels[i];
                longNames |= IsLongName(channel.Name);
                if (header.IsTiled || header.IsDeep)
                {
                    if (channel.XSampling != 1 || channel.YSampling != 1)
                    {
                        throw Invalid("Tiled and deep parts require unit channel sampling.");
                    }
                }
                else if (header.DataWindow.MinX % channel.XSampling != 0 ||
                    header.DataWindow.MinY % channel.YSampling != 0 ||
                    header.DataWindow.Width % channel.XSampling != 0 ||
                    header.DataWindow.Height % channel.YSampling != 0)
                {
                    throw Invalid(
                        $"Flat scanline channel '{channel.Name}' sampling does not align to the data window.");
                }
            }

            for (int i = 0; i < header.Attributes.Count; i++)
            {
                HeaderAttribute attribute = header.Attributes[i];
                if (ReservedAttributeNames.Contains(attribute.Name))
                {
                    continue;
                }

                longNames |= IsLongName(attribute.Name) || IsLongName(attribute.TypeName);
            }
        }

        private static void ValidateWindow(Box2i window)
        {
            if (window.MinX <= -WindowCoordinateLimit ||
                window.MinY <= -WindowCoordinateLimit ||
                window.MaxX >= WindowCoordinateLimit ||
                window.MaxY >= WindowCoordinateLimit)
            {
                throw Invalid("EXR window coordinates must remain inside the portable file-format range.");
            }
        }

        private static bool IsLongName(string value)
        {
            return ModelValidation.StrictUtf8.GetByteCount(value) > ShortNameByteLimit;
        }

        private static void WriteHeader(
            Stream output,
            Header header,
            bool multipart,
            int blockCount,
            out long? maximumSamplesPosition)
        {
            maximumSamplesPosition = null;
            using (MemoryStream channels = new MemoryStream())
            {
                Span<byte> descriptor = stackalloc byte[16];
                for (int i = 0; i < header.Channels.Count; i++)
                {
                    Channel channel = header.Channels[i];
                    WriteCString(channels, channel.Name);
                    descriptor.Clear();
                    BinaryPrimitives.WriteInt32LittleEndian(descriptor, (int)channel.PixelType);
                    descriptor[4] = channel.PerceptuallyLinear ? (byte)1 : (byte)0;
                    BinaryPrimitives.WriteInt32LittleEndian(descriptor.Slice(8), channel.XSampling);
                    BinaryPrimitives.WriteInt32LittleEndian(descriptor.Slice(12), channel.YSampling);
                    channels.Write(descriptor);
                }

                channels.WriteByte(0);
                WriteAttribute(output, "channels", "chlist", channels.ToArray());
            }

            Span<byte> scalar = stackalloc byte[4];
            scalar[0] = (byte)header.Compression;
            WriteAttribute(output, "compression", "compression", scalar.Slice(0, 1));
            WriteBox(output, "dataWindow", header.DataWindow);
            WriteBox(output, "displayWindow", header.DisplayWindow);
            scalar[0] = (byte)header.LineOrder;
            WriteAttribute(output, "lineOrder", "lineOrder", scalar.Slice(0, 1));
            WriteSingle(scalar, header.PixelAspectRatio);
            WriteAttribute(output, "pixelAspectRatio", "float", scalar);

            Span<byte> vector = stackalloc byte[8];
            WriteSingle(vector, header.ScreenWindowCenter.X);
            WriteSingle(vector.Slice(4), header.ScreenWindowCenter.Y);
            WriteAttribute(output, "screenWindowCenter", "v2f", vector);
            WriteSingle(scalar, header.ScreenWindowWidth);
            WriteAttribute(output, "screenWindowWidth", "float", scalar);

            if (header.IsTiled)
            {
                TileDescription tiles = header.Tiles!;
                Span<byte> value = stackalloc byte[9];
                BinaryPrimitives.WriteUInt32LittleEndian(value, tiles.TileSizeX);
                BinaryPrimitives.WriteUInt32LittleEndian(value.Slice(4), tiles.TileSizeY);
                value[8] = (byte)(((int)tiles.LevelMode & 0x0f) | ((int)tiles.RoundingMode << 4));
                WriteAttribute(output, "tiles", "tiledesc", value);
            }

            if (multipart)
            {
                WriteAttribute(output, "name", "string", ModelValidation.StrictUtf8.GetBytes(header.Name));
                WriteAttribute(
                    output,
                    "type",
                    "string",
                    ModelValidation.StrictUtf8.GetBytes(GetPartTypeName(header.PartType)));
                BinaryPrimitives.WriteInt32LittleEndian(scalar, blockCount);
                WriteAttribute(output, "chunkCount", "int", scalar);
            }

            if (header.IsDeep)
            {
                if (!multipart)
                {
                    WriteAttribute(
                        output,
                        "type",
                        "string",
                        ModelValidation.StrictUtf8.GetBytes(GetPartTypeName(header.PartType)));
                }

                BinaryPrimitives.WriteInt32LittleEndian(scalar, 1);
                WriteAttribute(output, "version", "int", scalar);
                BinaryPrimitives.WriteInt32LittleEndian(scalar, 0);
                maximumSamplesPosition = WriteAttribute(
                    output,
                    "maxSamplesPerPixel",
                    "int",
                    scalar);
            }

            if (header.Chromaticities.HasValue)
            {
                Chromaticities chromaticities = header.Chromaticities.Value;
                Span<byte> value = stackalloc byte[32];
                WriteSingle(value, chromaticities.RedX);
                WriteSingle(value.Slice(4), chromaticities.RedY);
                WriteSingle(value.Slice(8), chromaticities.GreenX);
                WriteSingle(value.Slice(12), chromaticities.GreenY);
                WriteSingle(value.Slice(16), chromaticities.BlueX);
                WriteSingle(value.Slice(20), chromaticities.BlueY);
                WriteSingle(value.Slice(24), chromaticities.WhiteX);
                WriteSingle(value.Slice(28), chromaticities.WhiteY);
                WriteAttribute(output, "chromaticities", "chromaticities", value);
            }

            for (int i = 0; i < header.Attributes.Count; i++)
            {
                HeaderAttribute attribute = header.Attributes[i];
                if (!ReservedAttributeNames.Contains(attribute.Name))
                {
                    WriteAttribute(output, attribute.Name, attribute.TypeName, attribute.Data);
                }
            }

            output.WriteByte(0);
        }

        private static string GetPartTypeName(PartType partType)
        {
            switch (partType)
            {
                case PartType.Scanline:
                    return "scanlineimage";
                case PartType.Tiled:
                    return "tiledimage";
                case PartType.DeepScanline:
                    return "deepscanline";
                case PartType.DeepTiled:
                    return "deeptile";
                default:
                    throw Invalid("The part type is not supported.");
            }
        }

        private static void WriteBox(Stream output, string name, Box2i value)
        {
            Span<byte> data = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(data, value.MinX);
            BinaryPrimitives.WriteInt32LittleEndian(data.Slice(4), value.MinY);
            BinaryPrimitives.WriteInt32LittleEndian(data.Slice(8), value.MaxX);
            BinaryPrimitives.WriteInt32LittleEndian(data.Slice(12), value.MaxY);
            WriteAttribute(output, name, "box2i", data);
        }

        private static long WriteAttribute(
            Stream output,
            string name,
            string typeName,
            ReadOnlySpan<byte> value)
        {
            WriteCString(output, name);
            WriteCString(output, typeName);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            output.Write(length);
            long valuePosition = output.Position;
            output.Write(value);
            return valuePosition;
        }

        private static void WriteCString(Stream output, string value)
        {
            byte[] bytes = ModelValidation.StrictUtf8.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
            output.WriteByte(0);
        }

        private static void WriteUInt32(Stream output, uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            output.Write(bytes);
        }

        private static void WriteSingle(Span<byte> destination, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));
        }

        private static void Limit(string name, long actual, long maximum)
        {
            if (actual > maximum)
            {
                throw new WriterPlanException(
                    ExrResult.Unsupported,
                    $"Writer limit '{name}' was exceeded.",
                    new WriterLimitExceededException(name, actual, maximum));
            }
        }

        private static WriterPlanException Invalid(string message, Exception? innerException = null)
        {
            return new WriterPlanException(ExrResult.InvalidArgument, message, innerException);
        }

        private static WriterPlanException Unsupported(string message)
        {
            return new WriterPlanException(
                ExrResult.Unsupported,
                message,
                new NotSupportedException(message));
        }
    }
}
