using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TinyEXR.V3.Codecs
{
    internal static partial class Htj2kDecoder
    {
        private const uint Htj2kDecompositionCount = 5;
        private const uint Htj2kCodeBlockWidth = 128;
        private const uint Htj2kCodeBlockHeight = 32;

        internal static Htj2kEncodeStatus Encode(
            Header header,
            Box2i region,
            byte[] source,
            out byte[] payload,
            out string? error)
        {
            return Encode(header, region, source, null, out payload, out error);
        }

        /// <summary>
        /// Encodes one HTJ2K block. Passing a caller-owned <paramref name="pool"/> lets every block of a part reuse
        /// the same buffers; a null pool creates and drops a private pool for this call alone.
        /// </summary>
        internal static Htj2kEncodeStatus Encode(
            Header header,
            Box2i region,
            byte[] source,
            Htj2kBufferPool? pool,
            out byte[] payload,
            out string? error)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            try
            {
                payload = pool == null
                    ? EncodeCore(header, region, source)
                    : EncodeCore(header, region, source, pool);
                error = null;
                return Htj2kEncodeStatus.Success;
            }
            catch (Htj2kEncodeException exception)
            {
                payload = Array.Empty<byte>();
                error = exception.Message;
                return exception.Status;
            }
            catch (OverflowException)
            {
                payload = Array.Empty<byte>();
                error = "The HTJ2K block exceeds managed dimensions or offsets.";
                return Htj2kEncodeStatus.Unsupported;
            }
            catch (IndexOutOfRangeException)
            {
                payload = Array.Empty<byte>();
                error = "The HTJ2K encoder encountered inconsistent block geometry.";
                return Htj2kEncodeStatus.Corrupt;
            }
        }

        private static byte[] EncodeCore(Header header, Box2i region, byte[] source)
        {
            using Htj2kBufferPool pool = new Htj2kBufferPool();
            return EncodeCore(header, region, source, pool);
        }

        private static byte[] EncodeCore(Header header, Box2i region, byte[] source, Htj2kBufferPool pool)
        {
            EncodeRequire(!header.IsDeep, Htj2kEncodeStatus.Unsupported,
                "HTJ2K encoding is available only for flat parts.");
            EncodeRequire(header.Channels.Count > 0 && header.Channels.Count <= ushort.MaxValue,
                Htj2kEncodeStatus.Unsupported,
                "The HTJ2K component count is outside the JPEG 2000 profile range.");

            uint width = checked((uint)region.Width);
            uint height = checked((uint)region.Height);
            EncodeRequire(width != 0 && height != 0,
                Htj2kEncodeStatus.InvalidArgument,
                "The HTJ2K block has zero dimensions.");

            ushort[] componentToFile = new ushort[header.Channels.Count];
            bool useColorTransform = MakeChannelMap(header.Channels, componentToFile);
            uint kmax = 20;
            for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
            {
                Channel channel = header.Channels[channelIndex];
                EncodeRequire(channel.PixelType == PixelType.Half ||
                    channel.PixelType == PixelType.Float ||
                    channel.PixelType == PixelType.UInt,
                    Htj2kEncodeStatus.Unsupported,
                    $"EXR pixel type '{channel.PixelType}' is not supported by HTJ2K.");
                EncodeRequire(channel.XSampling > 0 && channel.XSampling <= byte.MaxValue &&
                    channel.YSampling > 0 && channel.YSampling <= byte.MaxValue,
                    Htj2kEncodeStatus.Unsupported,
                    $"Channel '{channel.Name}' sampling is outside the HTJ2K profile range.");
                if (channel.PixelType != PixelType.Half)
                {
                    kmax = 33;
                }
            }

            Plane[] planes = DeinterleaveBlock(header, region, source, pool);
            try
            {
                ForwardNonlinearTransforms(header, planes);
                if (useColorTransform)
                {
                    ApplyForwardColorTransform(planes, componentToFile);
                }

                int temporaryLength = 0;
                uint maximumWidth = 0;
                foreach (Plane plane in planes)
                {
                    temporaryLength = Math.Max(temporaryLength, plane.ElementCount);
                    maximumWidth = Math.Max(maximumWidth, plane.Width);
                }

                long[] temporary = pool.RentInt64(temporaryLength);
                long[] lowRow = pool.RentInt64(checked((int)((maximumWidth + 1) / 2)));
                long[] highRow = pool.RentInt64(checked((int)(maximumWidth / 2)));
                try
                {
                    for (int component = 0; component < planes.Length; component++)
                    {
                        Forward53TwoDimensional(
                            planes[component].Data,
                            planes[component].Width,
                            planes[component].Height,
                            Htj2kDecompositionCount,
                            temporary,
                            lowRow,
                            highRow);
                    }
                }
                finally
                {
                    pool.ReturnInt64(highRow);
                    pool.ReturnInt64(lowRow);
                    pool.ReturnInt64(temporary);
                }

                using ByteAccumulator output = new ByteAccumulator(
                    Math.Min(checked(source.Length + 4096), 64 * 1024),
                    pool);
                WriteHtHeader(output, header, componentToFile);
                output.WriteUInt16BigEndian(MarkerSoc);
                WriteSiz(output, header, width, height, componentToFile);
                WriteCap(output);
                WriteCod(output, useColorTransform);
                WriteQcd(output, kmax);
                WriteNltMarkers(output, header, componentToFile);

                int sotStart = output.Count;
                output.WriteUInt16BigEndian(MarkerSot);
                output.WriteUInt16BigEndian(10);
                output.WriteUInt16BigEndian(0);
                int psotOffset = output.Count;
                output.WriteUInt32BigEndian(0);
                output.WriteByte(0);
                output.WriteByte(1);
                output.WriteUInt16BigEndian(MarkerSod);

                HtBlockEncoder blockEncoder = new HtBlockEncoder(EncoderTableData);
                for (uint resolution = 0; resolution <= Htj2kDecompositionCount; resolution++)
                {
                    for (int component = 0; component < componentToFile.Length; component++)
                    {
                        Plane plane = planes[componentToFile[component]];
                        WritePacketForComponentResolution(
                            output,
                            plane,
                            new Size(plane.Width, plane.Height),
                            resolution,
                            kmax,
                            blockEncoder);
                    }
                }

                output.PatchUInt32BigEndian(psotOffset, checked((uint)(output.Count - sotStart)));
                output.WriteUInt16BigEndian(MarkerEoc);
                return output.ToArray();
            }
            finally
            {
                ReturnPlanes(planes, pool);
            }
        }

        private static Plane[] DeinterleaveBlock(Header header, Box2i region, byte[] source, Htj2kBufferPool pool)
        {
            Plane[] planes = new Plane[header.Channels.Count];
            bool completed = false;
            try
            {
                for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
                {
                    Channel channel = header.Channels[channelIndex];
                    long width = ModelValidation.CountSampleLocations(
                        region.MinX,
                        region.MaxX,
                        channel.XSampling);
                    long height = ModelValidation.CountSampleLocations(
                        region.MinY,
                        region.MaxY,
                        channel.YSampling);
                    EncodeRequire(width > 0 && height > 0 && width <= int.MaxValue && height <= int.MaxValue,
                        Htj2kEncodeStatus.Unsupported,
                        $"Channel '{channel.Name}' has unsupported HTJ2K component dimensions.");
                    long count = checked(width * height);
                    EncodeRequire(count <= int.MaxValue,
                        Htj2kEncodeStatus.Unsupported,
                        $"Channel '{channel.Name}' exceeds managed HTJ2K component storage.");
                    planes[channelIndex] = new Plane(
                        checked((uint)width),
                        checked((uint)height),
                        pool.RentInt64(checked((int)count)));
                }

                int offset = 0;
                uint[] rows = new uint[planes.Length];
                for (long y = region.MinY; y <= region.MaxY; y++)
                {
                    for (int channelIndex = 0; channelIndex < header.Channels.Count; channelIndex++)
                    {
                        Channel channel = header.Channels[channelIndex];
                        if (y % channel.YSampling != 0)
                        {
                            continue;
                        }

                        Plane plane = planes[channelIndex];
                        EncodeRequire(rows[channelIndex] < plane.Height,
                            Htj2kEncodeStatus.InvalidArgument,
                            "The canonical EXR block contains too many sampled rows.");
                        int destinationOffset = checked((int)(rows[channelIndex] * plane.Width));
                        int rowWidth = checked((int)plane.Width);

                        // The pixel type and the truncation check are uniform across a row, so both are
                        // resolved once here instead of once per sample.
                        int sampleSize = channel.PixelType switch
                        {
                            PixelType.Half => 2,
                            PixelType.UInt => 4,
                            PixelType.Float => 4,
                            _ => throw new Htj2kEncodeException(
                                Htj2kEncodeStatus.Unsupported,
                                $"EXR pixel type '{channel.PixelType}' is not supported by HTJ2K."),
                        };
                        int rowBytes = checked(rowWidth * sampleSize);
                        EncodeRequire(rowBytes <= source.Length - offset,
                            Htj2kEncodeStatus.InvalidArgument,
                            "The canonical EXR block is truncated.");

                        ReadOnlySpan<byte> row = source.AsSpan(offset, rowBytes);
                        Span<long> destination = plane.Data.AsSpan(destinationOffset, rowWidth);
                        switch (channel.PixelType)
                        {
                            case PixelType.Half:
                                for (int x = 0; x < rowWidth; x++)
                                {
                                    destination[x] = BinaryPrimitives.ReadInt16LittleEndian(
                                        row.Slice(x * 2, 2));
                                }

                                break;
                            case PixelType.UInt:
                                for (int x = 0; x < rowWidth; x++)
                                {
                                    destination[x] = BinaryPrimitives.ReadUInt32LittleEndian(
                                        row.Slice(x * 4, 4));
                                }

                                break;
                            default:
                                for (int x = 0; x < rowWidth; x++)
                                {
                                    destination[x] = BinaryPrimitives.ReadInt32LittleEndian(
                                        row.Slice(x * 4, 4));
                                }

                                break;
                        }

                        offset += rowBytes;
                        rows[channelIndex]++;
                    }
                }

                EncodeRequire(offset == source.Length,
                    Htj2kEncodeStatus.InvalidArgument,
                    "The canonical EXR block length does not match its channel geometry.");
                for (int component = 0; component < planes.Length; component++)
                {
                    EncodeRequire(rows[component] == planes[component].Height,
                        Htj2kEncodeStatus.InvalidArgument,
                        "The canonical EXR block contains too few sampled rows.");
                }

                completed = true;
                return planes;
            }
            finally
            {
                if (!completed)
                {
                    ReturnPlanes(planes, pool);
                }
            }
        }

        private static void ForwardNonlinearTransforms(Header header, Plane[] planes)
        {
            for (int component = 0; component < planes.Length; component++)
            {
                Channel channel = header.Channels[component];
                if (channel.PixelType == PixelType.UInt)
                {
                    continue;
                }

                int bitDepth = channel.PixelType == PixelType.Half ? 16 : 32;
                long bias = (1L << (bitDepth - 1)) + 1;
                Span<long> data = planes[component].Data.AsSpan(0, planes[component].ElementCount);
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] < 0)
                    {
                        data[i] = -data[i] - bias;
                    }
                }
            }
        }

        private static void ApplyForwardColorTransform(Plane[] planes, ushort[] componentToFile)
        {
            Plane red = planes[componentToFile[0]];
            Plane green = planes[componentToFile[1]];
            Plane blue = planes[componentToFile[2]];
            EncodeRequire(red.Width == green.Width && red.Width == blue.Width &&
                red.Height == green.Height && red.Height == blue.Height,
                Htj2kEncodeStatus.InvalidArgument,
                "The HTJ2K RGB components have inconsistent dimensions.");

            int elementCount = red.ElementCount;
            Span<long> redData = red.Data.AsSpan(0, elementCount);
            Span<long> greenData = green.Data.AsSpan(0, elementCount);
            Span<long> blueData = blue.Data.AsSpan(0, elementCount);
            for (int i = 0; i < redData.Length; i++)
            {
                long redValue = redData[i];
                long greenValue = greenData[i];
                long blueValue = blueData[i];
                redData[i] = (redValue + blueValue + 2 * greenValue) >> 2;
                greenData[i] = blueValue - greenValue;
                blueData[i] = redValue - greenValue;
            }
        }

        private static void Forward53TwoDimensional(
            long[] data,
            uint width,
            uint height,
            uint levels,
            long[] temporary,
            long[] lowRow,
            long[] highRow)
        {
            EncodeRequire(levels <= 32,
                Htj2kEncodeStatus.InvalidArgument,
                "The HTJ2K wavelet level count is invalid.");
            for (uint level = 1; level <= levels; level++)
            {
                uint currentWidth = DivideCeilingPowerOfTwo(width, level - 1);
                uint currentHeight = DivideCeilingPowerOfTwo(height, level - 1);
                uint lowWidth = (currentWidth + 1) / 2;
                uint highWidth = currentWidth / 2;
                uint lowHeight = (currentHeight + 1) / 2;
                uint highHeight = currentHeight / 2;
                EncodeRequire((long)currentWidth * currentHeight <= temporary.Length,
                    Htj2kEncodeStatus.Corrupt,
                    "The HTJ2K forward wavelet workspace is too small.");

                int rowLength = checked((int)currentWidth);
                for (uint high = 0; high < highHeight; high++)
                {
                    int firstEvenOffset = checked((int)(2 * high * width));
                    int secondEvenOffset = checked((int)(
                        2 * (high + 1 < lowHeight ? high + 1 : high) * width));
                    int oddOffset = checked((int)((2 * high + 1) * width));
                    int outputOffset = checked((int)((lowHeight + high) * currentWidth));

                    // Slicing each participating row to its exact length lets the bounds checks be
                    // hoisted out of the column loop instead of being repeated for all four accesses.
                    Span<long> output = temporary.AsSpan(outputOffset, rowLength);
                    ReadOnlySpan<long> odd = data.AsSpan(oddOffset, rowLength);
                    ReadOnlySpan<long> firstEven = data.AsSpan(firstEvenOffset, rowLength);
                    ReadOnlySpan<long> secondEven = data.AsSpan(secondEvenOffset, rowLength);
                    for (int column = 0; column < output.Length; column++)
                    {
                        output[column] = odd[column] -
                            ((firstEven[column] + secondEven[column]) >> 1);
                    }
                }

                for (uint low = 0; low < lowHeight; low++)
                {
                    int evenOffset = checked((int)(2 * low * width));
                    int outputOffset = checked((int)(low * currentWidth));
                    if (highHeight == 0)
                    {
                        Array.Copy(data, evenOffset, temporary, outputOffset, checked((int)currentWidth));
                        continue;
                    }

                    uint leftHigh = low > 0 ? low - 1 : 0;
                    uint rightHigh = low < highHeight ? low : highHeight - 1;
                    int leftOffset = checked((int)((lowHeight + leftHigh) * currentWidth));
                    int rightOffset = checked((int)((lowHeight + rightHigh) * currentWidth));
                    ReadOnlySpan<long> even = data.AsSpan(evenOffset, rowLength);
                    ReadOnlySpan<long> left = temporary.AsSpan(leftOffset, rowLength);
                    ReadOnlySpan<long> right = temporary.AsSpan(rightOffset, rowLength);
                    Span<long> output = temporary.AsSpan(outputOffset, rowLength);
                    for (int column = 0; column < output.Length; column++)
                    {
                        output[column] = even[column] +
                            ((left[column] + right[column] + 2) >> 2);
                    }
                }

                EncodeRequire(lowWidth <= lowRow.Length && highWidth <= highRow.Length,
                    Htj2kEncodeStatus.Corrupt,
                    "The HTJ2K forward wavelet row workspace is too small.");
                for (uint row = 0; row < currentHeight; row++)
                {
                    int inputOffset = checked((int)(row * currentWidth));
                    Forward53OneDimensional(
                        temporary,
                        inputOffset,
                        currentWidth,
                        lowRow,
                        highRow);
                    int outputOffset = checked((int)(row * width));
                    Array.Copy(lowRow, 0, data, outputOffset, checked((int)lowWidth));
                    Array.Copy(highRow, 0, data, outputOffset + checked((int)lowWidth), checked((int)highWidth));
                }
            }
        }

        private static void Forward53OneDimensional(
            long[] source,
            int sourceOffset,
            uint count,
            long[] low,
            long[] high)
        {
            uint lowCount = (count + 1) / 2;
            uint highCount = count / 2;
            EncodeRequire(low.Length >= lowCount && high.Length >= highCount,
                Htj2kEncodeStatus.Corrupt,
                "The HTJ2K wavelet row has inconsistent low/high dimensions.");
            for (uint i = 0; i < highCount; i++)
            {
                long firstEven = source[sourceOffset + checked((int)(2 * i))];
                long secondEven = source[sourceOffset + checked((int)(
                    2 * (i + 1 < lowCount ? i + 1 : i)))];
                high[i] = source[sourceOffset + checked((int)(2 * i + 1))] -
                    FloorDividePowerOfTwo(firstEven + secondEven, 1);
            }

            for (uint i = 0; i < lowCount; i++)
            {
                long left = highCount == 0 ? 0 : high[i > 0 ? i - 1 : 0];
                long right = highCount == 0 ? 0 : high[i < highCount ? i : highCount - 1];
                low[i] = source[sourceOffset + checked((int)(2 * i))] +
                    FloorDividePowerOfTwo(left + right + 2, 2);
            }
        }

        private static bool MakeChannelMap(IReadOnlyList<Channel> channels, ushort[] componentToFile)
        {
            for (int i = 0; i < componentToFile.Length; i++)
            {
                componentToFile[i] = checked((ushort)i);
            }

            if (!TryFindRgbChannels(channels, "r", "g", "b", out int red, out int green, out int blue) &&
                !TryFindRgbChannels(channels, "red", "green", "blue", out red, out green, out blue))
            {
                return false;
            }

            Channel redChannel = channels[red];
            Channel greenChannel = channels[green];
            Channel blueChannel = channels[blue];
            if (redChannel.PixelType != greenChannel.PixelType ||
                redChannel.PixelType != blueChannel.PixelType ||
                redChannel.XSampling != greenChannel.XSampling ||
                redChannel.XSampling != blueChannel.XSampling ||
                redChannel.YSampling != greenChannel.YSampling ||
                redChannel.YSampling != blueChannel.YSampling)
            {
                return false;
            }

            componentToFile[0] = checked((ushort)red);
            componentToFile[1] = checked((ushort)green);
            componentToFile[2] = checked((ushort)blue);
            int output = 3;
            for (int i = 0; i < channels.Count; i++)
            {
                if (i != red && i != green && i != blue)
                {
                    componentToFile[output++] = checked((ushort)i);
                }
            }

            return true;
        }

        private static bool TryFindRgbChannels(
            IReadOnlyList<Channel> channels,
            string redSuffix,
            string greenSuffix,
            string blueSuffix,
            out int red,
            out int green,
            out int blue)
        {
            red = -1;
            green = -1;
            blue = -1;
            string? prefix = null;
            for (int i = 0; i < channels.Count; i++)
            {
                string name = channels[i].Name;
                int dot = name.LastIndexOf('.');
                string currentPrefix = dot >= 0 ? name.Substring(0, dot) : string.Empty;
                string suffix = dot >= 0 ? name.Substring(dot + 1) : name;
                bool matchesRed = red < 0 && string.Equals(suffix, redSuffix, StringComparison.OrdinalIgnoreCase);
                bool matchesGreen = green < 0 && string.Equals(suffix, greenSuffix, StringComparison.OrdinalIgnoreCase);
                bool matchesBlue = blue < 0 && string.Equals(suffix, blueSuffix, StringComparison.OrdinalIgnoreCase);
                if (!matchesRed && !matchesGreen && !matchesBlue)
                {
                    continue;
                }

                if (prefix != null && !string.Equals(prefix, currentPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                prefix = currentPrefix;
                if (matchesRed)
                {
                    red = i;
                }
                else if (matchesGreen)
                {
                    green = i;
                }
                else
                {
                    blue = i;
                }
            }

            return red >= 0 && green >= 0 && blue >= 0;
        }

        private static void WriteHtHeader(
            ByteAccumulator output,
            Header header,
            ushort[] componentToFile)
        {
            output.WriteUInt16BigEndian(0x4854);
            output.WriteUInt32BigEndian(checked((uint)(2 + header.Channels.Count * 2)));
            output.WriteUInt16BigEndian(checked((ushort)header.Channels.Count));
            for (int i = 0; i < componentToFile.Length; i++)
            {
                output.WriteUInt16BigEndian(componentToFile[i]);
            }
        }

        private static void WriteSiz(
            ByteAccumulator output,
            Header header,
            uint width,
            uint height,
            ushort[] componentToFile)
        {
            output.WriteUInt16BigEndian(MarkerSiz);
            output.WriteUInt16BigEndian(checked((ushort)(38 + header.Channels.Count * 3)));
            output.WriteUInt16BigEndian(0x4000);
            output.WriteUInt32BigEndian(width);
            output.WriteUInt32BigEndian(height);
            output.WriteUInt32BigEndian(0);
            output.WriteUInt32BigEndian(0);
            output.WriteUInt32BigEndian(width);
            output.WriteUInt32BigEndian(height);
            output.WriteUInt32BigEndian(0);
            output.WriteUInt32BigEndian(0);
            output.WriteUInt16BigEndian(checked((ushort)header.Channels.Count));
            for (int component = 0; component < componentToFile.Length; component++)
            {
                Channel channel = header.Channels[componentToFile[component]];
                int bitDepth = channel.PixelType == PixelType.Half ? 16 : 32;
                int signed = channel.PixelType == PixelType.UInt ? 0 : 1;
                output.WriteByte(checked((byte)((signed << 7) | (bitDepth - 1))));
                output.WriteByte(checked((byte)channel.XSampling));
                output.WriteByte(checked((byte)channel.YSampling));
            }
        }

        private static void WriteCap(ByteAccumulator output)
        {
            output.WriteUInt16BigEndian(MarkerCap);
            output.WriteUInt16BigEndian(8);
            output.WriteUInt32BigEndian(0x00020000);
            output.WriteUInt16BigEndian(0x000b);
        }

        private static void WriteCod(ByteAccumulator output, bool useColorTransform)
        {
            output.WriteUInt16BigEndian(MarkerCod);
            output.WriteUInt16BigEndian(12);
            output.WriteByte(0);
            output.WriteByte(2);
            output.WriteUInt16BigEndian(1);
            output.WriteByte(useColorTransform ? (byte)1 : (byte)0);
            output.WriteByte(checked((byte)Htj2kDecompositionCount));
            output.WriteByte(5);
            output.WriteByte(3);
            output.WriteByte(0x40);
            output.WriteByte(1);
        }

        private static void WriteQcd(ByteAccumulator output, uint kmax)
        {
            uint guardBits = 1;
            uint exponent = kmax;
            if (kmax > 30)
            {
                exponent = 30;
                guardBits = kmax - 29;
            }

            EncodeRequire(guardBits <= 7 && exponent <= 31,
                Htj2kEncodeStatus.InvalidArgument,
                "The HTJ2K quantization precision is outside the profile range.");
            output.WriteUInt16BigEndian(MarkerQcd);
            output.WriteUInt16BigEndian(19);
            output.WriteByte(checked((byte)(guardBits << 5)));
            byte value = checked((byte)(exponent << 3));
            for (int i = 0; i < 16; i++)
            {
                output.WriteByte(value);
            }
        }

        private static void WriteNltMarkers(
            ByteAccumulator output,
            Header header,
            ushort[] componentToFile)
        {
            for (int component = 0; component < componentToFile.Length; component++)
            {
                Channel channel = header.Channels[componentToFile[component]];
                int bitDepth = channel.PixelType == PixelType.Half ? 16 : 32;
                int signed = channel.PixelType == PixelType.UInt ? 0 : 1;
                output.WriteUInt16BigEndian(MarkerNlt);
                output.WriteUInt16BigEndian(6);
                output.WriteUInt16BigEndian(checked((ushort)component));
                output.WriteByte(checked((byte)((signed << 7) | (bitDepth - 1))));
                output.WriteByte(signed != 0 ? (byte)3 : (byte)0);
            }
        }

        private static void WritePacketForComponentResolution(
            ByteAccumulator output,
            Plane plane,
            Size componentSize,
            uint resolution,
            uint kmax,
            HtBlockEncoder blockEncoder)
        {
            PacketWriter packet = new PacketWriter(output);
            List<EncodedCodeBlock> body = new List<EncodedCodeBlock>();
            bool sawNonempty = false;
            uint skippedSubbands = 0;
            BandGeometry[] bands = BuildEncodingBandGeometries(componentSize, resolution, kmax);
            int firstBand = resolution == 0 ? 0 : 1;
            int lastBand = resolution == 0 ? 0 : 3;
            for (int bandIndex = firstBand; bandIndex <= lastBand; bandIndex++)
            {
                BandGeometry band = bands[bandIndex];
                if (!band.Exists)
                {
                    continue;
                }

                uint codeBlocksWide = DivideCeiling(band.Width, Htj2kCodeBlockWidth);
                uint codeBlocksHigh = DivideCeiling(band.Height, Htj2kCodeBlockHeight);
                int codeBlockCount = checked((int)(codeBlocksWide * codeBlocksHigh));
                EncodedCodeBlock?[] encoded = new EncodedCodeBlock?[codeBlockCount];
                uint[] inclusionValues = new uint[codeBlockCount];
                uint[] missingMsbValues = new uint[codeBlockCount];
                GetBandOffsets(componentSize, resolution, bandIndex, out uint rowOffset, out uint columnOffset);
                bool bandNonempty = false;

                for (uint codeBlockY = 0; codeBlockY < codeBlocksHigh; codeBlockY++)
                {
                    for (uint codeBlockX = 0; codeBlockX < codeBlocksWide; codeBlockX++)
                    {
                        uint localX = codeBlockX * Htj2kCodeBlockWidth;
                        uint localY = codeBlockY * Htj2kCodeBlockHeight;
                        uint blockWidth = Math.Min(Htj2kCodeBlockWidth, band.Width - localX);
                        uint blockHeight = Math.Min(Htj2kCodeBlockHeight, band.Height - localY);
                        int index = checked((int)(codeBlockY * codeBlocksWide + codeBlockX));
                        EncodedCodeBlock codeBlock = blockEncoder.Encode(
                            plane,
                            checked(columnOffset + localX),
                            checked(rowOffset + localY),
                            blockWidth,
                            blockHeight,
                            kmax);
                        if (codeBlock.Data.Length == 0)
                        {
                            inclusionValues[index] = 1;
                            missingMsbValues[index] = kmax;
                        }
                        else
                        {
                            encoded[index] = codeBlock;
                            inclusionValues[index] = 0;
                            missingMsbValues[index] = codeBlock.MissingMsbs;
                            bandNonempty = true;
                        }
                    }
                }

                if (!bandNonempty)
                {
                    if (sawNonempty)
                    {
                        packet.WriteBit(0);
                    }
                    else
                    {
                        skippedSubbands++;
                    }

                    continue;
                }

                if (!sawNonempty)
                {
                    packet.WriteBit(1);
                    packet.WriteZeros(skippedSubbands);
                    skippedSubbands = 0;
                    sawNonempty = true;
                }

                EncodingTagTree inclusionTree = new EncodingTagTree(
                    codeBlocksWide,
                    codeBlocksHigh,
                    inclusionValues);
                EncodingTagTree missingTree = new EncodingTagTree(
                    codeBlocksWide,
                    codeBlocksHigh,
                    missingMsbValues);
                for (uint codeBlockY = 0; codeBlockY < codeBlocksHigh; codeBlockY++)
                {
                    for (uint codeBlockX = 0; codeBlockX < codeBlocksWide; codeBlockX++)
                    {
                        int index = checked((int)(codeBlockY * codeBlocksWide + codeBlockX));
                        inclusionTree.WriteLeaf(packet, codeBlockX, codeBlockY, isMissingMsb: false);
                        EncodedCodeBlock? codeBlock = encoded[index];
                        if (codeBlock == null)
                        {
                            continue;
                        }

                        missingTree.WriteLeaf(packet, codeBlockX, codeBlockY, isMissingMsb: true);
                        packet.WriteBit(0);
                        packet.WritePassLengths(codeBlock.Length0, codeBlock.Length1, 1);
                    }
                }

                for (int i = 0; i < encoded.Length; i++)
                {
                    if (encoded[i] != null)
                    {
                        body.Add(encoded[i]!);
                    }
                }
            }

            if (!sawNonempty)
            {
                packet.WriteBit(0);
            }

            packet.Finish();
            for (int i = 0; i < body.Count; i++)
            {
                output.Write(body[i].Data);
            }
        }

        private static BandGeometry[] BuildEncodingBandGeometries(
            Size componentSize,
            uint resolution,
            uint kmax)
        {
            Size resolutionSize = ResolutionSize(componentSize, Htj2kDecompositionCount, resolution);
            BandGeometry[] bands = new BandGeometry[4];
            if (resolution == 0)
            {
                bands[0] = new BandGeometry(
                    resolutionSize.Width,
                    resolutionSize.Height,
                    Htj2kCodeBlockWidth,
                    Htj2kCodeBlockHeight,
                    kmax);
                return bands;
            }

            bands[1] = new BandGeometry(
                resolutionSize.Width >> 1,
                (resolutionSize.Height + 1) >> 1,
                Htj2kCodeBlockWidth,
                Htj2kCodeBlockHeight,
                kmax);
            bands[2] = new BandGeometry(
                (resolutionSize.Width + 1) >> 1,
                resolutionSize.Height >> 1,
                Htj2kCodeBlockWidth,
                Htj2kCodeBlockHeight,
                kmax);
            bands[3] = new BandGeometry(
                resolutionSize.Width >> 1,
                resolutionSize.Height >> 1,
                Htj2kCodeBlockWidth,
                Htj2kCodeBlockHeight,
                kmax);
            return bands;
        }

        private static void GetBandOffsets(
            Size componentSize,
            uint resolution,
            int band,
            out uint rowOffset,
            out uint columnOffset)
        {
            rowOffset = 0;
            columnOffset = 0;
            if (resolution == 0 || band == 0)
            {
                return;
            }

            Size resolutionSize = ResolutionSize(componentSize, Htj2kDecompositionCount, resolution);
            if (band == 1)
            {
                columnOffset = (resolutionSize.Width + 1) >> 1;
            }
            else if (band == 2)
            {
                rowOffset = (resolutionSize.Height + 1) >> 1;
            }
            else
            {
                rowOffset = (resolutionSize.Height + 1) >> 1;
                columnOffset = (resolutionSize.Width + 1) >> 1;
            }
        }

        private static EncoderTables EncoderTableData => EncoderTableHolder.Value;

        private static void EncodeRequire(bool condition, Htj2kEncodeStatus status, string message)
        {
            if (!condition)
            {
                throw new Htj2kEncodeException(status, message);
            }
        }

        private sealed class ByteAccumulator : IDisposable
        {
            private readonly Htj2kBufferPool _pool;
            private byte[] _data;

            public ByteAccumulator(int initialCapacity, Htj2kBufferPool pool)
            {
                _pool = pool;
                _data = pool.RentBytes(Math.Max(initialCapacity, 256));
            }

            public int Count { get; private set; }

            public void WriteByte(byte value)
            {
                Ensure(1);
                _data[Count++] = value;
            }

            public void WriteUInt16BigEndian(ushort value)
            {
                Ensure(2);
                BinaryPrimitives.WriteUInt16BigEndian(_data.AsSpan(Count, 2), value);
                Count += 2;
            }

            public void WriteUInt32BigEndian(uint value)
            {
                Ensure(4);
                BinaryPrimitives.WriteUInt32BigEndian(_data.AsSpan(Count, 4), value);
                Count += 4;
            }

            public void Write(byte[] value)
            {
                Ensure(value.Length);
                value.AsSpan().CopyTo(_data.AsSpan(Count));
                Count += value.Length;
            }

            public void PatchUInt32BigEndian(int offset, uint value)
            {
                EncodeRequire(offset >= 0 && offset <= Count - 4,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K marker patch lies outside the codestream.");
                BinaryPrimitives.WriteUInt32BigEndian(_data.AsSpan(offset, 4), value);
            }

            public byte[] ToArray()
            {
                byte[] result = new byte[Count];
                Array.Copy(_data, result, Count);
                return result;
            }

            private void Ensure(int additional)
            {
                int required = checked(Count + additional);
                if (required <= _data.Length)
                {
                    return;
                }

                int capacity = _data.Length;
                while (capacity < required)
                {
                    capacity = checked(capacity * 2);
                }

                byte[] replacement = _pool.RentBytes(capacity);
                Array.Copy(_data, replacement, Count);
                _pool.ReturnBytes(_data);
                _data = replacement;
            }

            public void Dispose()
            {
                if (_data.Length != 0)
                {
                    _pool.ReturnBytes(_data);
                    _data = Array.Empty<byte>();
                }
            }
        }

        private sealed class PacketWriter
        {
            private readonly ByteAccumulator _output;
            private byte _current;
            private byte _used;
            private bool _previousFf;

            public PacketWriter(ByteAccumulator output)
            {
                _output = output;
            }

            public void WriteBit(uint bit)
            {
                byte limit = _previousFf ? (byte)7 : (byte)8;
                _current = checked((byte)((_current << 1) | (int)(bit & 1u)));
                _used++;
                if (_used == limit)
                {
                    FlushByte();
                }
            }

            public void WriteBits(uint value, uint bitCount)
            {
                while (bitCount != 0)
                {
                    bitCount--;
                    WriteBit((value >> checked((int)bitCount)) & 1);
                }
            }

            public void WriteZeros(uint bitCount)
            {
                while (bitCount-- != 0)
                {
                    WriteBit(0);
                }
            }

            public void WritePassLengths(uint length0, uint length1, uint activePasses)
            {
                EncodeRequire(activePasses >= 1 && activePasses <= 3 && length0 != 0,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K codeblock has invalid pass lengths.");
                uint bits1 = EncodingBitLength(length0);
                uint bits2 = activePasses > 1 ? EncodingBitLength(length1) : 0;
                uint extraBit = activePasses > 2 ? 1u : 0u;
                uint bits = bits1;
                if (bits2 > extraBit && bits2 - extraBit > bits)
                {
                    bits = bits2 - extraBit;
                }

                bits = bits > 3 ? bits - 3 : 0;
                for (uint i = 0; i < bits; i++)
                {
                    WriteBit(1);
                }

                WriteBit(0);
                WriteBits(length0, bits + 3);
                if (activePasses > 1)
                {
                    WriteBits(length1, bits + 3 + extraBit);
                }
            }

            public void Finish()
            {
                if (_used != 0)
                {
                    FlushByte();
                }

                if (_previousFf)
                {
                    _output.WriteByte(0);
                    _previousFf = false;
                }
            }

            private void FlushByte()
            {
                byte limit = _previousFf ? (byte)7 : (byte)8;
                if (_used < limit)
                {
                    _current = checked((byte)(_current << (limit - _used)));
                }

                _output.WriteByte(_current);
                _previousFf = limit == 8 && _current == 0xff;
                _current = 0;
                _used = 0;
            }
        }

        private sealed class EncodingTagTree
        {
            private readonly uint[] _widths;
            private readonly uint[] _heights;
            private readonly int[] _offsets;
            private readonly uint[] _values;
            private readonly bool[] _known;

            public EncodingTagTree(uint width, uint height, uint[] leaves)
            {
                EncodeRequire(width != 0 && height != 0 && leaves.Length == checked((int)(width * height)),
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K tag-tree has invalid dimensions.");
                List<uint> widths = new List<uint>();
                List<uint> heights = new List<uint>();
                List<int> offsets = new List<int>();
                long count = 0;
                uint currentWidth = width;
                uint currentHeight = height;
                while (true)
                {
                    widths.Add(currentWidth);
                    heights.Add(currentHeight);
                    offsets.Add(checked((int)count));
                    count = checked(count + (long)currentWidth * currentHeight);
                    EncodeRequire(count <= int.MaxValue,
                        Htj2kEncodeStatus.Unsupported,
                        "An HTJ2K tag-tree exceeds managed storage.");
                    if (currentWidth == 1 && currentHeight == 1)
                    {
                        break;
                    }

                    currentWidth = (currentWidth + 1) / 2;
                    currentHeight = (currentHeight + 1) / 2;
                }

                _widths = widths.ToArray();
                _heights = heights.ToArray();
                _offsets = offsets.ToArray();
                _values = new uint[checked((int)count)];
                _known = new bool[_values.Length];
                Array.Copy(leaves, _values, leaves.Length);
                for (int level = 1; level < _widths.Length; level++)
                {
                    uint childWidth = _widths[level - 1];
                    uint childHeight = _heights[level - 1];
                    for (uint y = 0; y < _heights[level]; y++)
                    {
                        for (uint x = 0; x < _widths[level]; x++)
                        {
                            uint value = uint.MaxValue;
                            for (uint childY = y * 2; childY < Math.Min(childHeight, y * 2 + 2); childY++)
                            {
                                for (uint childX = x * 2; childX < Math.Min(childWidth, x * 2 + 2); childX++)
                                {
                                    int childIndex = checked(_offsets[level - 1] +
                                        (int)(childY * childWidth + childX));
                                    value = Math.Min(value, _values[childIndex]);
                                }
                            }

                            _values[checked(_offsets[level] +
                                (int)(y * _widths[level] + x))] = value;
                        }
                    }
                }
            }

            public void WriteLeaf(PacketWriter writer, uint leafX, uint leafY, bool isMissingMsb)
            {
                EncodeRequire(leafX < _widths[0] && leafY < _heights[0],
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K tag-tree leaf lies outside the tree.");
                for (int currentLevel = _widths.Length; currentLevel > 0; currentLevel--)
                {
                    int level = currentLevel - 1;
                    uint x = leafX >> level;
                    uint y = leafY >> level;
                    int index = checked(_offsets[level] + (int)(y * _widths[level] + x));
                    uint child = _values[index];
                    uint parent = 0;
                    if (currentLevel < _widths.Length)
                    {
                        uint parentX = leafX >> currentLevel;
                        uint parentY = leafY >> currentLevel;
                        int parentIndex = checked(_offsets[currentLevel] +
                            (int)(parentY * _widths[currentLevel] + parentX));
                        parent = _values[parentIndex];
                    }

                    if (!_known[index])
                    {
                        EncodeRequire(child >= parent,
                            Htj2kEncodeStatus.Corrupt,
                            "An HTJ2K tag-tree child is smaller than its parent.");
                        if (isMissingMsb)
                        {
                            writer.WriteZeros(child - parent);
                            writer.WriteBit(1);
                        }
                        else
                        {
                            EncodeRequire(child - parent <= 1,
                                Htj2kEncodeStatus.Corrupt,
                                "An HTJ2K inclusion tag-tree value is invalid.");
                            writer.WriteBit(1 - (child - parent));
                        }

                        _known[index] = true;
                    }

                    if (!isMissingMsb && child > 0)
                    {
                        break;
                    }
                }
            }
        }

        private sealed class HtBlockEncoder
        {
            private readonly EncoderTables _tables;
            private readonly byte[] _magnitudeBuffer = new byte[65536];
            private readonly byte[] _melBuffer = new byte[192];
            private readonly byte[] _vlcBuffer = new byte[3072 - 192];
            private readonly byte[] _exponentLine = new byte[513];
            private readonly byte[] _contextLine = new byte[513];

            public HtBlockEncoder(EncoderTables tables)
            {
                _tables = tables;
            }

            public EncodedCodeBlock Encode(
                Plane plane,
                uint blockX,
                uint blockY,
                uint width,
                uint height,
                uint kmax)
            {
                EncodeRequire(width != 0 && height != 0 &&
                    width <= Htj2kCodeBlockWidth && height <= Htj2kCodeBlockHeight,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K codeblock has invalid dimensions.");
                EncodeRequire(kmax >= 1 && kmax <= 36,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K codeblock has invalid precision.");

                bool use32BitPacking = kmax <= 30;
                int shift = checked((int)((use32BitPacking ? 31u : 63u) - kmax));
                int precision = shift;
                QuadContext quadContext = new QuadContext(
                    plane.Data,
                    checked((int)plane.Width),
                    blockX,
                    blockY,
                    width,
                    height,
                    shift,
                    precision,
                    use32BitPacking);
                ulong maximumValue = 0;
                MagSignWriter magnitude = new MagSignWriter(_magnitudeBuffer);
                MelWriter mel = new MelWriter(_melBuffer);
                VlcWriter vlc = new VlcWriter(_vlcBuffer);
                byte[] exponentLine = _exponentLine;
                byte[] contextLine = _contextLine;
                Array.Clear(exponentLine, 0, exponentLine.Length);
                Array.Clear(contextLine, 0, contextLine.Length);
                int exponentPosition = 0;
                int contextPosition = 0;
                int firstContext = 0;

                // These four scratch spans are reset for every quad pair. Stack slices keep the reset
                // as an inlined store instead of four Array.Clear calls into heap arrays, which a
                // 128x32 codeblock would otherwise make about two thousand times.
                Span<int> exponentMax = stackalloc int[2];
                Span<int> exponents = stackalloc int[8];
                Span<int> significance = stackalloc int[2];
                Span<ulong> samples = stackalloc ulong[8];
                for (uint x = 0; x < width; x += 4)
                {
                    exponentMax.Clear();
                    exponents.Clear();
                    significance.Clear();
                    samples.Clear();
                    PrepareQuad(
                        quadContext,
                        x,
                        0,
                        exponents,
                        0,
                        samples,
                        0,
                        ref significance[0],
                        ref exponentMax[0],
                        ref maximumValue);

                    int firstU = Math.Max(exponentMax[0], 1);
                    int firstUOffset = firstU - 1;
                    int firstEmbedding = BuildEmbedding(exponents, 0, exponentMax[0], firstUOffset);
                    exponentLine[exponentPosition] = Math.Max(
                        exponentLine[exponentPosition],
                        checked((byte)exponents[1]));
                    exponentPosition++;
                    exponentLine[exponentPosition] = checked((byte)exponents[3]);
                    contextLine[contextPosition] |= checked((byte)((significance[0] & 2) >> 1));
                    contextPosition++;
                    contextLine[contextPosition] = checked((byte)((significance[0] & 8) >> 3));

                    ushort firstTuple = GetVlcTuple(
                        _tables.Vlc0,
                        firstContext,
                        significance[0],
                        firstEmbedding);
                    vlc.Encode(firstTuple >> 8, (firstTuple >> 4) & 7);
                    if (firstContext == 0)
                    {
                        mel.Encode(significance[0] != 0 ? 1 : 0);
                    }

                    EncodeMagnitudeQuad(magnitude, samples, 0, significance[0], firstU, firstTuple);

                    int secondUOffset = 0;
                    if (x + 2 < width)
                    {
                        PrepareQuad(
                            quadContext,
                            x + 2,
                            0,
                            exponents,
                            4,
                            samples,
                            4,
                            ref significance[1],
                            ref exponentMax[1],
                            ref maximumValue);
                        int secondContext = (significance[0] >> 1) | (significance[0] & 1);
                        int secondU = Math.Max(exponentMax[1], 1);
                        secondUOffset = secondU - 1;
                        int secondEmbedding = BuildEmbedding(exponents, 4, exponentMax[1], secondUOffset);
                        exponentLine[exponentPosition] = Math.Max(
                            exponentLine[exponentPosition],
                            checked((byte)exponents[5]));
                        exponentPosition++;
                        exponentLine[exponentPosition] = checked((byte)exponents[7]);
                        contextLine[contextPosition] |= checked((byte)((significance[1] & 2) >> 1));
                        contextPosition++;
                        contextLine[contextPosition] = checked((byte)((significance[1] & 8) >> 3));

                        ushort secondTuple = GetVlcTuple(
                            _tables.Vlc0,
                            secondContext,
                            significance[1],
                            secondEmbedding);
                        vlc.Encode(secondTuple >> 8, (secondTuple >> 4) & 7);
                        if (secondContext == 0)
                        {
                            mel.Encode(significance[1] != 0 ? 1 : 0);
                        }

                        EncodeMagnitudeQuad(magnitude, samples, 4, significance[1], secondU, secondTuple);
                    }

                    if (firstUOffset > 0 && secondUOffset > 0)
                    {
                        mel.Encode(Math.Min(firstUOffset, secondUOffset) > 2 ? 1 : 0);
                    }

                    EncodeUvlcPair(vlc, firstUOffset, secondUOffset, initialLine: true);
                    firstContext = (significance[1] >> 1) | (significance[1] & 1);
                }

                exponentLine[exponentPosition + 1] = 0;
                for (uint y = 2; y < height; y += 2)
                {
                    exponentPosition = 0;
                    int maximumExponent = Math.Max(
                        exponentLine[exponentPosition],
                        exponentLine[exponentPosition + 1]) - 1;
                    exponentLine[exponentPosition] = 0;
                    contextPosition = 0;
                    firstContext = contextLine[contextPosition] +
                        (contextLine[contextPosition + 1] << 2);
                    contextLine[contextPosition] = 0;

                    for (uint x = 0; x < width; x += 4)
                    {
                        exponentMax.Clear();
                        exponents.Clear();
                        significance.Clear();
                        samples.Clear();
                        PrepareQuad(
                            quadContext,
                            x,
                            y,
                            exponents,
                            0,
                            samples,
                            0,
                            ref significance[0],
                            ref exponentMax[0],
                            ref maximumValue);
                        int kappa = (significance[0] & (significance[0] - 1)) != 0
                            ? Math.Max(maximumExponent, 1)
                            : 1;
                        int firstU = Math.Max(exponentMax[0], kappa);
                        int firstUOffset = firstU - kappa;
                        int firstEmbedding = BuildEmbedding(exponents, 0, exponentMax[0], firstUOffset);
                        exponentLine[exponentPosition] = Math.Max(
                            exponentLine[exponentPosition],
                            checked((byte)exponents[1]));
                        exponentPosition++;
                        maximumExponent = Math.Max(
                            exponentLine[exponentPosition],
                            exponentLine[exponentPosition + 1]) - 1;
                        exponentLine[exponentPosition] = checked((byte)exponents[3]);
                        contextLine[contextPosition] |= checked((byte)((significance[0] & 2) >> 1));
                        contextPosition++;
                        int secondContext = contextLine[contextPosition] +
                            (contextLine[contextPosition + 1] << 2);
                        contextLine[contextPosition] = checked((byte)((significance[0] & 8) >> 3));

                        ushort firstTuple = GetVlcTuple(
                            _tables.Vlc1,
                            firstContext,
                            significance[0],
                            firstEmbedding);
                        vlc.Encode(firstTuple >> 8, (firstTuple >> 4) & 7);
                        if (firstContext == 0)
                        {
                            mel.Encode(significance[0] != 0 ? 1 : 0);
                        }

                        EncodeMagnitudeQuad(magnitude, samples, 0, significance[0], firstU, firstTuple);

                        int secondUOffset = 0;
                        if (x + 2 < width)
                        {
                            PrepareQuad(
                                quadContext,
                                x + 2,
                                y,
                                exponents,
                                4,
                                samples,
                                4,
                                ref significance[1],
                                ref exponentMax[1],
                                ref maximumValue);
                            kappa = (significance[1] & (significance[1] - 1)) != 0
                                ? Math.Max(maximumExponent, 1)
                                : 1;
                            secondContext |= ((significance[0] & 4) >> 1) |
                                ((significance[0] & 8) >> 2);
                            int secondU = Math.Max(exponentMax[1], kappa);
                            secondUOffset = secondU - kappa;
                            int secondEmbedding = BuildEmbedding(
                                exponents,
                                4,
                                exponentMax[1],
                                secondUOffset);
                            exponentLine[exponentPosition] = Math.Max(
                                exponentLine[exponentPosition],
                                checked((byte)exponents[5]));
                            exponentPosition++;
                            maximumExponent = Math.Max(
                                exponentLine[exponentPosition],
                                exponentLine[exponentPosition + 1]) - 1;
                            exponentLine[exponentPosition] = checked((byte)exponents[7]);
                            contextLine[contextPosition] |= checked((byte)((significance[1] & 2) >> 1));
                            contextPosition++;
                            firstContext = contextLine[contextPosition] +
                                (contextLine[contextPosition + 1] << 2);
                            contextLine[contextPosition] = checked((byte)((significance[1] & 8) >> 3));

                            ushort secondTuple = GetVlcTuple(
                                _tables.Vlc1,
                                secondContext,
                                significance[1],
                                secondEmbedding);
                            vlc.Encode(secondTuple >> 8, (secondTuple >> 4) & 7);
                            if (secondContext == 0)
                            {
                                mel.Encode(significance[1] != 0 ? 1 : 0);
                            }

                            EncodeMagnitudeQuad(
                                magnitude,
                                samples,
                                4,
                                significance[1],
                                secondU,
                                secondTuple);
                        }

                        EncodeUvlcPair(vlc, firstUOffset, secondUOffset, initialLine: false);
                        firstContext |= ((significance[1] & 4) >> 1) |
                            ((significance[1] & 8) >> 2);
                    }
                }

                if (maximumValue == 0)
                {
                    return new EncodedCodeBlock(Array.Empty<byte>(), kmax, 0, 0);
                }

                EncodeRequire(maximumValue < (1UL << checked((int)kmax)),
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K coefficient exceeds the declared precision.");
                magnitude.Terminate();
                TerminateMelAndVlc(mel, vlc);

                int cleanupLength = checked(magnitude.Position + mel.Position + vlc.Position);
                int suffixLength = checked(mel.Position + vlc.Position);
                if (cleanupLength < 2)
                {
                    cleanupLength = 2;
                    suffixLength = 2;
                }

                EncodeRequire(cleanupLength < 65535,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K cleanup pass exceeds the profile length limit.");
                byte[] result = new byte[cleanupLength];
                Array.Copy(_magnitudeBuffer, 0, result, 0, magnitude.Position);
                Array.Copy(_melBuffer, 0, result, magnitude.Position, mel.Position);
                Array.Copy(
                    _vlcBuffer,
                    _vlcBuffer.Length - vlc.Position,
                    result,
                    magnitude.Position + mel.Position,
                    vlc.Position);
                result[cleanupLength - 2] = checked((byte)(
                    (result[cleanupLength - 2] & 0xf0) | (suffixLength & 0x0f)));
                result[cleanupLength - 1] = checked((byte)((suffixLength >> 4) & 0xff));
                return new EncodedCodeBlock(
                    result,
                    kmax - 1,
                    checked((uint)cleanupLength),
                    0);
            }

            private ushort GetVlcTuple(ushort[] table, int context, int significance, int embedding)
            {
                EncodeRequire(context >= 0 && context < 8 &&
                    significance >= 0 && significance < 16 &&
                    embedding >= 0 && embedding < 16,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K VLC tuple index is invalid.");
                ushort tuple = table[(context << 8) | (significance << 4) | embedding];
                EncodeRequire(tuple != 0 || (context == 0 && significance == 0 && embedding == 0),
                    Htj2kEncodeStatus.Corrupt,
                    "The HTJ2K VLC table has no codeword for a generated tuple.");
                return tuple;
            }

            /// <summary>
            /// The per-codeblock values that every quad and sample packing step shares.
            /// </summary>
            /// <remarks>
            /// Grouping these into one readonly struct keeps the quad and sample signatures short
            /// enough to stay register-based. Passed by <c>in</c> so no copy is made per call.
            /// </remarks>
            private readonly ref struct QuadContext
            {
                public QuadContext(
                    ReadOnlySpan<long> planeData,
                    int planeWidth,
                    uint blockX,
                    uint blockY,
                    uint width,
                    uint height,
                    int shift,
                    int precision,
                    bool use32BitPacking)
                {
                    PlaneData = planeData;
                    PlaneWidth = planeWidth;
                    BlockX = blockX;
                    BlockY = blockY;
                    Width = width;
                    Height = height;
                    Shift = shift;
                    Precision = precision;
                    Use32BitPacking = use32BitPacking;
                }

                public ReadOnlySpan<long> PlaneData { get; }

                public int PlaneWidth { get; }

                public uint BlockX { get; }

                public uint BlockY { get; }

                public uint Width { get; }

                public uint Height { get; }

                public int Shift { get; }

                public int Precision { get; }

                public bool Use32BitPacking { get; }
            }

            /// <summary>
            /// The packed form of one coefficient.
            /// </summary>
            private readonly struct PreparedSample
            {
                public PreparedSample(int exponent, ulong sample, ulong magnitude, bool isSignificant)
                {
                    Exponent = exponent;
                    Sample = sample;
                    Magnitude = magnitude;
                    IsSignificant = isSignificant;
                }

                public int Exponent { get; }

                public ulong Sample { get; }

                public ulong Magnitude { get; }

                public bool IsSignificant { get; }
            }

            /// <summary>
            /// Packs the up to four coefficients of one HT quad.
            /// </summary>
            /// <remarks>
            /// The quad's top-left index is resolved once here rather than recomputing a full
            /// <c>(blockY + y) * width + blockX + x</c> product per coefficient. The four accumulators
            /// are kept in locals and written back once, so the per-sample step no longer reads and
            /// writes them through <c>ref</c> into stack memory on every call.
            /// </remarks>
            private static void PrepareQuad(
                in QuadContext context,
                uint x,
                uint y,
                Span<int> exponents,
                int exponentOffset,
                Span<ulong> samples,
                int sampleOffset,
                ref int significance,
                ref int maximumExponent,
                ref ulong maximumValue)
            {
                int planeWidth = context.PlaneWidth;
                int index = checked((int)((context.BlockY + y) * (uint)planeWidth + context.BlockX + x));
                bool hasLowerRow = y + 1 < context.Height;
                bool hasRightColumn = x + 1 < context.Width;

                int localSignificance = significance;
                int localMaximumExponent = maximumExponent;
                ulong localMaximumValue = maximumValue;

                PreparedSample sample = PrepareSample(context, context.PlaneData[index]);
                Accumulate(
                    sample,
                    1,
                    ref localSignificance,
                    ref localMaximumExponent,
                    ref localMaximumValue);
                exponents[exponentOffset] = sample.Exponent;
                samples[sampleOffset] = sample.Sample;

                if (hasLowerRow)
                {
                    sample = PrepareSample(context, context.PlaneData[index + planeWidth]);
                    Accumulate(
                        sample,
                        2,
                        ref localSignificance,
                        ref localMaximumExponent,
                        ref localMaximumValue);
                    exponents[exponentOffset + 1] = sample.Exponent;
                    samples[sampleOffset + 1] = sample.Sample;
                }

                if (hasRightColumn)
                {
                    sample = PrepareSample(context, context.PlaneData[index + 1]);
                    Accumulate(
                        sample,
                        4,
                        ref localSignificance,
                        ref localMaximumExponent,
                        ref localMaximumValue);
                    exponents[exponentOffset + 2] = sample.Exponent;
                    samples[sampleOffset + 2] = sample.Sample;

                    if (hasLowerRow)
                    {
                        sample = PrepareSample(context, context.PlaneData[index + planeWidth + 1]);
                        Accumulate(
                            sample,
                            8,
                            ref localSignificance,
                            ref localMaximumExponent,
                            ref localMaximumValue);
                        exponents[exponentOffset + 3] = sample.Exponent;
                        samples[sampleOffset + 3] = sample.Sample;
                    }
                }

                significance = localSignificance;
                maximumExponent = localMaximumExponent;
                maximumValue = localMaximumValue;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Accumulate(
                PreparedSample sample,
                int significanceBit,
                ref int significance,
                ref int maximumExponent,
                ref ulong maximumValue)
            {
                if (sample.Magnitude > maximumValue)
                {
                    maximumValue = sample.Magnitude;
                }

                if (!sample.IsSignificant)
                {
                    return;
                }

                significance |= significanceBit;
                if (sample.Exponent > maximumExponent)
                {
                    maximumExponent = sample.Exponent;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static PreparedSample PrepareSample(in QuadContext context, long signedValue)
            {
                int shift = context.Shift;
                int precision = context.Precision;
                ulong magnitude = AbsoluteAsUInt64(signedValue);
                if (context.Use32BitPacking)
                {
                    uint packed = (signedValue < 0 ? 0x80000000u : 0u) |
                        unchecked((uint)magnitude << shift);
                    uint value = unchecked(packed + packed);
                    value >>= precision;
                    value &= ~1u;
                    if (value == 0)
                    {
                        return new PreparedSample(0, 0, magnitude, isSignificant: false);
                    }

                    value--;
                    int packedExponent = checked((int)EncodingBitLength(value));
                    value--;
                    return new PreparedSample(
                        packedExponent,
                        unchecked(value + (packed >> 31)),
                        magnitude,
                        isSignificant: true);
                }

                ulong packed64 = (signedValue < 0 ? 0x8000000000000000UL : 0UL) |
                    unchecked(magnitude << shift);
                ulong value64 = unchecked(packed64 + packed64);
                value64 >>= precision;
                value64 &= ~1UL;
                if (value64 == 0)
                {
                    return new PreparedSample(0, 0, magnitude, isSignificant: false);
                }

                value64--;
                int exponent64 = checked((int)EncodingBitLength(value64));
                value64--;
                return new PreparedSample(
                    exponent64,
                    unchecked(value64 + (packed64 >> 63)),
                    magnitude,
                    isSignificant: true);
            }

            private static int BuildEmbedding(
                ReadOnlySpan<int> exponents,
                int offset,
                int maximumExponent,
                int uOffset)
            {
                if (uOffset <= 0)
                {
                    return 0;
                }

                int embedding = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (exponents[offset + i] == maximumExponent)
                    {
                        embedding |= 1 << i;
                    }
                }

                return embedding;
            }

            private void EncodeUvlcPair(VlcWriter vlc, int first, int second, bool initialLine)
            {
                EncodeRequire(first >= 0 && first < _tables.Uvlc.Length &&
                    second >= 0 && second < _tables.Uvlc.Length,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K UVLC value is outside the encoding table.");
                if (initialLine && first > 0 && second > 0)
                {
                    if (first > 2 && second > 2)
                    {
                        UvlcCode firstCode = _tables.Uvlc[first - 2];
                        UvlcCode secondCode = _tables.Uvlc[second - 2];
                        vlc.Encode(firstCode.Prefix, firstCode.PrefixLength);
                        vlc.Encode(secondCode.Prefix, secondCode.PrefixLength);
                        vlc.Encode(firstCode.Suffix, firstCode.SuffixLength);
                        vlc.Encode(secondCode.Suffix, secondCode.SuffixLength);
                        vlc.Encode(firstCode.Extension, firstCode.ExtensionLength);
                        vlc.Encode(secondCode.Extension, secondCode.ExtensionLength);
                        return;
                    }

                    if (first > 2)
                    {
                        UvlcCode code = _tables.Uvlc[first];
                        vlc.Encode(code.Prefix, code.PrefixLength);
                        vlc.Encode(second - 1, 1);
                        vlc.Encode(code.Suffix, code.SuffixLength);
                        vlc.Encode(code.Extension, code.ExtensionLength);
                        return;
                    }
                }

                UvlcCode firstValue = _tables.Uvlc[first];
                UvlcCode secondValue = _tables.Uvlc[second];
                vlc.Encode(firstValue.Prefix, firstValue.PrefixLength);
                vlc.Encode(secondValue.Prefix, secondValue.PrefixLength);
                vlc.Encode(firstValue.Suffix, firstValue.SuffixLength);
                vlc.Encode(secondValue.Suffix, secondValue.SuffixLength);
                vlc.Encode(firstValue.Extension, firstValue.ExtensionLength);
                vlc.Encode(secondValue.Extension, secondValue.ExtensionLength);
            }

            private static void EncodeMagnitudeQuad(
                MagSignWriter writer,
                ReadOnlySpan<ulong> samples,
                int offset,
                int significance,
                int u,
                ushort tuple)
            {
                EncodeMagnitudePair(
                    writer,
                    samples[offset],
                    samples[offset + 1],
                    significance,
                    1,
                    2,
                    u,
                    tuple);
                EncodeMagnitudePair(
                    writer,
                    samples[offset + 2],
                    samples[offset + 3],
                    significance,
                    4,
                    8,
                    u,
                    tuple);
            }

            private static void EncodeMagnitudePair(
                MagSignWriter writer,
                ulong first,
                ulong second,
                int significance,
                int firstBit,
                int secondBit,
                int u,
                ushort tuple)
            {
                int firstLength = (significance & firstBit) != 0
                    ? u - ((tuple & firstBit) != 0 ? 1 : 0)
                    : 0;
                int secondLength = (significance & secondBit) != 0
                    ? u - ((tuple & secondBit) != 0 ? 1 : 0)
                    : 0;
                EncodeRequire(firstLength >= 0 && firstLength < 64 &&
                    secondLength >= 0 && secondLength < 64,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K MagSgn codeword has invalid length.");
                if (firstLength == 0)
                {
                    writer.Encode(second & Mask64(checked((uint)secondLength)), secondLength);
                }
                else if (secondLength == 0)
                {
                    writer.Encode(first & Mask64(checked((uint)firstLength)), firstLength);
                }
                else if (firstLength + secondLength < 64)
                {
                    ulong codeword = first & Mask64(checked((uint)firstLength));
                    codeword |= (second & Mask64(checked((uint)secondLength))) << firstLength;
                    writer.Encode(codeword, firstLength + secondLength);
                }
                else
                {
                    writer.Encode(first & Mask64(checked((uint)firstLength)), firstLength);
                    writer.Encode(second & Mask64(checked((uint)secondLength)), secondLength);
                }
            }

            private static void TerminateMelAndVlc(MelWriter mel, VlcWriter vlc)
            {
                if (mel.Run > 0)
                {
                    mel.EmitBit(1);
                }

                mel.Temp <<= mel.RemainingBits;
                int melMask = (0xff << mel.RemainingBits) & 0xff;
                int vlcMask = 0xff >> (8 - vlc.UsedBits);
                if ((melMask | vlcMask) == 0)
                {
                    return;
                }

                int vlcTemporary = checked((int)(vlc.Temp & 0xff));
                int fused = mel.Temp | vlcTemporary;
                if ((((fused ^ mel.Temp) & melMask) |
                    ((fused ^ vlcTemporary) & vlcMask)) == 0 &&
                    fused != 0xff &&
                    vlc.Position > 1)
                {
                    mel.AppendTerminator(checked((byte)fused));
                }
                else
                {
                    mel.AppendTerminator(checked((byte)mel.Temp));
                    vlc.PrependTerminator(checked((byte)vlcTemporary));
                }
            }
        }

        private sealed class MagSignWriter
        {
            private readonly byte[] _buffer;
            private int _current;
            private int _usedBits;
            private int _maximumBits = 8;

            public MagSignWriter(byte[] buffer)
            {
                _buffer = buffer;
            }

            public int Position { get; private set; }

            /// <summary>
            /// Appends the low <paramref name="length"/> bits of <paramref name="codeword"/>.
            /// </summary>
            /// <remarks>
            /// Bits are transferred in runs that fill the pending byte rather than one at a time.
            /// The run width has to be recomputed after every flush because bit stuffing shrinks the
            /// next byte to seven bits whenever the byte just emitted was 0xff.
            /// </remarks>
            public void Encode(ulong codeword, int length)
            {
                int bit = 0;
                while (bit < length)
                {
                    int available = _maximumBits - _usedBits;
                    int take = length - bit;
                    if (take > available)
                    {
                        take = available;
                    }

                    int chunk = unchecked((int)((codeword >> bit) & ((1UL << take) - 1)));
                    _current |= chunk << _usedBits;
                    _usedBits += take;
                    bit += take;
                    if (_usedBits == _maximumBits)
                    {
                        Flush();
                    }
                }
            }

            public void Terminate()
            {
                if (_usedBits != 0)
                {
                    int trailing = _maximumBits - _usedBits;
                    int value = _current | (((1 << trailing) - 1) << _usedBits);
                    if (value != 0xff)
                    {
                        Append(checked((byte)value));
                    }
                }
                else if (_maximumBits == 7 && Position > 0)
                {
                    Position--;
                }
            }

            private void Flush()
            {
                byte value = checked((byte)_current);
                Append(value);
                _maximumBits = value == 0xff ? 7 : 8;
                _current = 0;
                _usedBits = 0;
            }

            private void Append(byte value)
            {
                EncodeRequire(Position < _buffer.Length,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K MagSgn segment exceeds its maximum size.");
                _buffer[Position++] = value;
            }
        }

        private sealed class MelWriter
        {
            private static readonly int[] Exponents = { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4, 5 };
            private readonly byte[] _buffer;
            private int _k;
            private int _threshold = 1;

            public MelWriter(byte[] buffer)
            {
                _buffer = buffer;
            }

            public int Position { get; private set; }

            public int RemainingBits { get; set; } = 8;

            public int Temp { get; set; }

            public int Run { get; private set; }

            public void Encode(int bit)
            {
                if (bit == 0)
                {
                    Run++;
                    if (Run >= _threshold)
                    {
                        EmitBit(1);
                        Run = 0;
                        _k = Math.Min(_k + 1, 12);
                        _threshold = 1 << Exponents[_k];
                    }

                    return;
                }

                EmitBit(0);
                int bits = Exponents[_k];
                while (bits > 0)
                {
                    bits--;
                    EmitBit((Run >> bits) & 1);
                }

                Run = 0;
                _k = Math.Max(_k - 1, 0);
                _threshold = 1 << Exponents[_k];
            }

            public void EmitBit(int value)
            {
                Temp = (Temp << 1) + value;
                RemainingBits--;
                if (RemainingBits == 0)
                {
                    AppendTerminator(checked((byte)Temp));
                    RemainingBits = Temp == 0xff ? 7 : 8;
                    Temp = 0;
                }
            }

            public void AppendTerminator(byte value)
            {
                EncodeRequire(Position < _buffer.Length,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K MEL segment exceeds its maximum size.");
                _buffer[Position++] = value;
            }
        }

        private sealed class VlcWriter
        {
            private readonly byte[] _buffer;
            private bool _lastGreaterThan8f = true;

            public VlcWriter(byte[] buffer)
            {
                _buffer = buffer;
                _buffer[_buffer.Length - 1] = 0xff;
            }

            public int Position { get; private set; } = 1;

            public int UsedBits { get; private set; } = 4;

            public ulong Temp { get; private set; } = 0x0f;

            public void Encode(int codeword, int length)
            {
                if (length <= 0)
                {
                    return;
                }

                Temp |= (ulong)(uint)codeword << UsedBits;
                UsedBits += length;
                while (UsedBits >= 8)
                {
                    byte value;
                    if (_lastGreaterThan8f)
                    {
                        value = checked((byte)(Temp & 0x7f));
                        if (value == 0x7f)
                        {
                            Prepend(value);
                            _lastGreaterThan8f = false;
                            Temp >>= 7;
                            UsedBits -= 7;
                            continue;
                        }
                    }

                    value = checked((byte)(Temp & 0xff));
                    Prepend(value);
                    _lastGreaterThan8f = value > 0x8f;
                    Temp >>= 8;
                    UsedBits -= 8;
                }
            }

            public void PrependTerminator(byte value)
            {
                Prepend(value);
            }

            private void Prepend(byte value)
            {
                EncodeRequire(Position < _buffer.Length,
                    Htj2kEncodeStatus.Corrupt,
                    "An HTJ2K VLC segment exceeds its maximum size.");
                _buffer[_buffer.Length - 1 - Position] = value;
                Position++;
            }
        }

        private sealed class EncoderTables
        {
            private EncoderTables(ushort[] vlc0, ushort[] vlc1, UvlcCode[] uvlc)
            {
                Vlc0 = vlc0;
                Vlc1 = vlc1;
                Uvlc = uvlc;
            }

            public ushort[] Vlc0 { get; }

            public ushort[] Vlc1 { get; }

            public UvlcCode[] Uvlc { get; }

            public static EncoderTables Create(HtTables decoderTables)
            {
                UvlcCode[] uvlc = new UvlcCode[75];
                uvlc[0] = new UvlcCode(0, 0, 0, 0, 0, 0);
                uvlc[1] = new UvlcCode(1, 1, 0, 0, 0, 0);
                uvlc[2] = new UvlcCode(2, 2, 0, 0, 0, 0);
                uvlc[3] = new UvlcCode(4, 3, 0, 1, 0, 0);
                uvlc[4] = new UvlcCode(4, 3, 1, 1, 0, 0);
                for (int value = 5; value < 33; value++)
                {
                    uvlc[value] = new UvlcCode(0, 3, value - 5, 5, 0, 0);
                }

                for (int value = 33; value < 75; value++)
                {
                    uvlc[value] = new UvlcCode(
                        0,
                        3,
                        28 + (value - 33) % 4,
                        5,
                        (value - 33) / 4,
                        4);
                }

                return new EncoderTables(
                    BuildVlcEncoderTable(decoderTables.Vlc0),
                    BuildVlcEncoderTable(decoderTables.Vlc1),
                    uvlc);
            }

            private static ushort[] BuildVlcEncoderTable(ushort[] decoderTable)
            {
                ushort[] result = new ushort[2048];
                for (int context = 0; context < 8; context++)
                {
                    for (int significance = 0; significance < 16; significance++)
                    {
                        for (int embedding = 0; embedding < 16; embedding++)
                        {
                            if ((embedding & significance) != embedding ||
                                (significance == 0 && context == 0))
                            {
                                continue;
                            }

                            int bestScore = -1;
                            ushort best = 0;
                            for (int bits = 0; bits < 128; bits++)
                            {
                                ushort decoded = decoderTable[(context << 7) | bits];
                                int length = decoded & 7;
                                if (length == 0 || ((decoded >> 4) & 15) != significance)
                                {
                                    continue;
                                }

                                int uOffset = (decoded >> 3) & 1;
                                int embeddingMask = (decoded >> 12) & 15;
                                int embeddingValue = (decoded >> 8) & 15;
                                if (embedding == 0)
                                {
                                    if (uOffset != 0)
                                    {
                                        continue;
                                    }

                                    int codeword = bits & ((1 << length) - 1);
                                    best = checked((ushort)((codeword << 8) |
                                        (length << 4) | embeddingMask));
                                    break;
                                }

                                if (uOffset == 0 ||
                                    (embedding & embeddingMask) != embeddingValue)
                                {
                                    continue;
                                }

                                int score = checked((int)PopCount(checked((uint)embeddingMask)));
                                if (score > bestScore)
                                {
                                    int codeword = bits & ((1 << length) - 1);
                                    best = checked((ushort)((codeword << 8) |
                                        (length << 4) | embeddingMask));
                                    bestScore = score;
                                }
                            }

                            result[(context << 8) | (significance << 4) | embedding] = best;
                        }
                    }
                }

                return result;
            }
        }

        private static class EncoderTableHolder
        {
            internal static readonly EncoderTables Value = EncoderTables.Create(Tables);
        }

        private readonly struct UvlcCode
        {
            public UvlcCode(
                int prefix,
                int prefixLength,
                int suffix,
                int suffixLength,
                int extension,
                int extensionLength)
            {
                Prefix = prefix;
                PrefixLength = prefixLength;
                Suffix = suffix;
                SuffixLength = suffixLength;
                Extension = extension;
                ExtensionLength = extensionLength;
            }

            public int Prefix { get; }

            public int PrefixLength { get; }

            public int Suffix { get; }

            public int SuffixLength { get; }

            public int Extension { get; }

            public int ExtensionLength { get; }
        }

        private sealed class EncodedCodeBlock
        {
            public EncodedCodeBlock(byte[] data, uint missingMsbs, uint length0, uint length1)
            {
                Data = data;
                MissingMsbs = missingMsbs;
                Length0 = length0;
                Length1 = length1;
            }

            public byte[] Data { get; }

            public uint MissingMsbs { get; }

            public uint Length0 { get; }

            public uint Length1 { get; }
        }

        /// <summary>
        /// Returns the number of significant bits in <paramref name="value"/>, or zero when it is zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint EncodingBitLength(uint value)
        {
            return 32u - CountLeadingZeros(value);
        }

        /// <summary>
        /// Returns the number of significant bits in <paramref name="value"/>, or zero when it is zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint EncodingBitLength(ulong value)
        {
            return 64u - CountLeadingZeros(value);
        }

        private static ulong AbsoluteAsUInt64(long value)
        {
            ulong mask = unchecked((ulong)(value >> 63));
            return unchecked((unchecked((ulong)value) ^ mask) - mask);
        }

        private sealed class Htj2kEncodeException : Exception
        {
            public Htj2kEncodeException(Htj2kEncodeStatus status, string message)
                : base(message)
            {
                Status = status;
            }

            public Htj2kEncodeStatus Status { get; }
        }
    }

    internal enum Htj2kEncodeStatus
    {
        Success,
        InvalidArgument,
        Unsupported,
        Corrupt,
    }
}
