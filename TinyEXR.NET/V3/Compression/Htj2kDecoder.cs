using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TinyEXR.V3.Codecs
{
    /// <summary>
    /// Scalar, allocation-bounded decoder for the OpenEXR HTJ2K profile.
    ///
    /// The HT block coding and table derivation follow ITU-T T.814 and the
    /// BSD-licensed OpenJPH/TinyEXR implementations. This implementation keeps
    /// all state in managed arrays and intentionally omits native SIMD paths.
    /// </summary>
    internal static partial class Htj2kDecoder
    {
        private const ushort MarkerSoc = 0xff4f;
        private const ushort MarkerCap = 0xff50;
        private const ushort MarkerSiz = 0xff51;
        private const ushort MarkerCod = 0xff52;
        private const ushort MarkerCoc = 0xff53;
        private const ushort MarkerTlm = 0xff55;
        private const ushort MarkerPrf = 0xff56;
        private const ushort MarkerPlm = 0xff57;
        private const ushort MarkerPlt = 0xff58;
        private const ushort MarkerCpf = 0xff59;
        private const ushort MarkerQcd = 0xff5c;
        private const ushort MarkerQcc = 0xff5d;
        private const ushort MarkerRgn = 0xff5e;
        private const ushort MarkerPoc = 0xff5f;
        private const ushort MarkerPpm = 0xff60;
        private const ushort MarkerPpt = 0xff61;
        private const ushort MarkerCrg = 0xff63;
        private const ushort MarkerCom = 0xff64;
        private const ushort MarkerDfs = 0xff72;
        private const ushort MarkerAds = 0xff73;
        private const ushort MarkerNlt = 0xff76;
        private const ushort MarkerAtk = 0xff79;
        private const ushort MarkerSot = 0xff90;
        private const ushort MarkerSod = 0xff93;
        private const ushort MarkerEoc = 0xffd9;

        internal static Htj2kDecodeStatus Decode(
            Header header,
            Box2i region,
            byte[] source,
            byte[] destination,
            out string? error)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            return Decode(header, region, source, destination.AsSpan(), out error);
        }

        internal static Htj2kDecodeStatus Decode(
            Header header,
            Box2i region,
            byte[] source,
            Span<byte> destination,
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
                DecodeCore(new DecodeContext(header, region), source, destination);
                error = null;
                return Htj2kDecodeStatus.Success;
            }
            catch (Htj2kDecodeException exception)
            {
                error = exception.Message;
                return exception.Status;
            }
            catch (OverflowException)
            {
                error = "The HTJ2K payload contains overflowing dimensions or offsets.";
                return Htj2kDecodeStatus.Corrupt;
            }
            catch (IndexOutOfRangeException)
            {
                error = "The HTJ2K payload references data outside a validated segment.";
                return Htj2kDecodeStatus.Corrupt;
            }
        }

        private static void DecodeCore(DecodeContext context, byte[] source, Span<byte> destination)
        {
            int codestreamOffset = ParseHtHeader(context, source, out ushort[] channelMap);
            Require(codestreamOffset < source.Length, "The HTJ2K wrapper has no JPEG 2000 codestream.");

            Profile profile = ParseProfile(source, codestreamOffset, source.Length);
            ValidateProfile(context, profile, channelMap);
            DecodeTilePayload(context, profile, channelMap, source, destination);
        }

        private static int ParseHtHeader(DecodeContext context, byte[] source, out ushort[] channelMap)
        {
            Require(source.Length >= 8, "The HTJ2K wrapper is truncated.");
            Require(ReadUInt16BigEndian(source, 0) == 0x4854, "The HTJ2K wrapper magic is invalid.");

            uint payloadSize = ReadUInt32BigEndian(source, 2);
            long headerSize = 6L + payloadSize;
            Require(payloadSize >= 2 && headerSize <= source.Length, "The HTJ2K wrapper length is invalid.");

            ushort channelCount = ReadUInt16BigEndian(source, 6);
            Require(channelCount == context.Channels.Count, "The HTJ2K channel map count does not match the EXR header.");
            Require(payloadSize >= 2u + (uint)channelCount * 2u, "The HTJ2K channel map is truncated.");

            channelMap = new ushort[channelCount];
            bool[] seen = new bool[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                ushort fileChannel = ReadUInt16BigEndian(source, 8 + i * 2);
                Require(fileChannel < channelCount && !seen[fileChannel], "The HTJ2K channel map is not a permutation.");
                channelMap[i] = fileChannel;
                seen[fileChannel] = true;
            }

            return checked((int)headerSize);
        }

        private static Profile ParseProfile(byte[] source, int start, int end)
        {
            ByteReader reader = new ByteReader(source, start, end);
            Require(reader.NextMarker() == MarkerSoc, "The HTJ2K codestream does not start with SOC.");
            Require(reader.NextMarker() == MarkerSiz, "The HTJ2K codestream does not start with SIZ.");

            Profile profile = ParseSiz(reader);
            bool done = false;
            while (!done)
            {
                ushort marker = reader.NextMarker();
                switch (marker)
                {
                    case MarkerCap:
                        ParseCap(reader, profile);
                        break;
                    case MarkerPrf:
                    case MarkerCpf:
                    case MarkerTlm:
                    case MarkerPlm:
                    case MarkerPlt:
                    case MarkerCrg:
                    case MarkerCom:
                        reader.SkipSegment();
                        break;
                    case MarkerCod:
                        ParseCod(reader, profile);
                        break;
                    case MarkerQcd:
                        ParseQuantization(reader, profile, componentSpecific: false);
                        profile.SawQcd = true;
                        break;
                    case MarkerQcc:
                        Require(profile.SawSiz, "QCC occurs before SIZ.");
                        ParseQuantization(reader, profile, componentSpecific: true);
                        break;
                    case MarkerNlt:
                        ParseNlt(reader, profile);
                        break;
                    case MarkerSot:
                        profile.SotStart = reader.Position - 2;
                        ParseSot(reader, profile);
                        while (true)
                        {
                            marker = reader.NextMarker();
                            if (marker == MarkerSod)
                            {
                                profile.SawSod = true;
                                FinishTilePayload(reader, profile);
                                done = true;
                                break;
                            }

                            if (marker == MarkerPlt || marker == MarkerCom ||
                                marker == MarkerDfs || marker == MarkerAds ||
                                marker == MarkerAtk || marker == MarkerPoc)
                            {
                                reader.SkipSegment();
                                continue;
                            }

                            Unsupported($"JPEG 2000 tile marker 0x{marker:x4} is not supported by the OpenEXR HTJ2K profile.");
                        }

                        break;
                    case MarkerCoc:
                    case MarkerRgn:
                    case MarkerPoc:
                    case MarkerPpm:
                    case MarkerPpt:
                    case MarkerDfs:
                    case MarkerAds:
                    case MarkerAtk:
                        reader.SkipSegment();
                        break;
                    default:
                        Unsupported($"JPEG 2000 marker 0x{marker:x4} is not supported by the OpenEXR HTJ2K profile.");
                        break;
                }
            }

            return profile;
        }

        private static Profile ParseSiz(ByteReader reader)
        {
            ushort length = reader.ReadUInt16BigEndian();
            Require(length >= 38, "The JPEG 2000 SIZ marker is too short.");
            int segmentEnd = reader.SegmentEnd(length);

            Profile profile = new Profile
            {
                Rsiz = reader.ReadUInt16BigEndian(),
                Xsiz = reader.ReadUInt32BigEndian(),
                Ysiz = reader.ReadUInt32BigEndian(),
                Xosiz = reader.ReadUInt32BigEndian(),
                Yosiz = reader.ReadUInt32BigEndian(),
                Xtsiz = reader.ReadUInt32BigEndian(),
                Ytsiz = reader.ReadUInt32BigEndian(),
                Xtosiz = reader.ReadUInt32BigEndian(),
                Ytosiz = reader.ReadUInt32BigEndian(),
            };

            ushort componentCount = reader.ReadUInt16BigEndian();
            Require(componentCount != 0, "The JPEG 2000 SIZ marker has no components.");
            Require(segmentEnd - reader.Position == componentCount * 3, "The JPEG 2000 SIZ component data has an invalid length.");

            profile.Ssiz = new byte[componentCount];
            profile.Xrsiz = new byte[componentCount];
            profile.Yrsiz = new byte[componentCount];
            profile.NltType = new byte[componentCount];
            profile.QccExponents = new byte[componentCount][];
            profile.QccCounts = new byte[componentCount];
            profile.QccGuardBits = new byte[componentCount];
            for (int i = 0; i < componentCount; i++)
            {
                profile.Ssiz[i] = reader.ReadByte();
                profile.Xrsiz[i] = reader.ReadByte();
                profile.Yrsiz[i] = reader.ReadByte();
                profile.QccExponents[i] = new byte[16];
            }

            profile.SawSiz = true;
            return profile;
        }

        private static void ParseCap(ByteReader reader, Profile profile)
        {
            ushort length = reader.ReadUInt16BigEndian();
            Require(length >= 6 && ((length - 6) & 1) == 0, "The JPEG 2000 CAP marker has an invalid length.");
            int segmentEnd = reader.SegmentEnd(length);
            uint capabilities = reader.ReadUInt32BigEndian();
            Support((capabilities & 0x00020000u) != 0, "The JPEG 2000 codestream does not advertise HT block coding.");
            reader.Position = segmentEnd;
            profile.SawCap = true;
        }

        private static void ParseCod(ByteReader reader, Profile profile)
        {
            ushort length = reader.ReadUInt16BigEndian();
            Require(length >= 12, "The JPEG 2000 COD marker is too short.");
            int segmentEnd = reader.SegmentEnd(length);
            byte codingStyle = reader.ReadByte();
            byte progression = reader.ReadByte();
            ushort layers = reader.ReadUInt16BigEndian();
            byte multipleComponentTransform = reader.ReadByte();
            byte decompositions = reader.ReadByte();
            byte codeblockWidth = reader.ReadByte();
            byte codeblockHeight = reader.ReadByte();
            byte codeblockStyle = reader.ReadByte();
            byte transform = reader.ReadByte();

            Support(codingStyle == 0, "Non-default JPEG 2000 precincts are not supported.");
            Support(progression == 2, "Only RPCL progression is supported by the OpenEXR HTJ2K profile.");
            Support(layers == 1, "Only one JPEG 2000 quality layer is supported.");
            Require(multipleComponentTransform <= 1, "The JPEG 2000 color transform selector is invalid.");
            Support(decompositions == 5, "The OpenEXR HTJ2K profile requires five wavelet decompositions.");
            Support(codeblockWidth == 5 && codeblockHeight == 3, "The JPEG 2000 codeblock dimensions are not 128x32.");
            Support((codeblockStyle & 0x40) != 0, "The JPEG 2000 codestream does not select HT block coding.");
            Support((codeblockStyle & ~0x40) == 0, "Unsupported JPEG 2000 codeblock style flags are present.");
            Support(transform == 1, "Only the reversible JPEG 2000 5/3 transform is supported.");
            Require(reader.Position == segmentEnd, "The JPEG 2000 COD marker has unexpected trailing data.");

            profile.NumDecompositions = decompositions;
            profile.MultipleComponentTransform = multipleComponentTransform;
            profile.SawCod = true;
        }

        private static void ParseQuantization(ByteReader reader, Profile profile, bool componentSpecific)
        {
            ushort length = reader.ReadUInt16BigEndian();
            Require(length >= (componentSpecific ? 4 : 3), "The JPEG 2000 quantization marker is too short.");
            int segmentEnd = reader.SegmentEnd(length);

            int component = -1;
            if (componentSpecific)
            {
                component = profile.ComponentCount < 257
                    ? reader.ReadByte()
                    : reader.ReadUInt16BigEndian();
                Require(component >= 0 && component < profile.ComponentCount, "The JPEG 2000 QCC component index is invalid.");
            }

            byte style = reader.ReadByte();
            Support((style & 0x1f) == 0, "Only JPEG 2000 no-quantization style is supported.");
            byte guardBits = (byte)(style >> 5);
            int count = segmentEnd - reader.Position;
            Require(count > 0 && count <= 16, "The JPEG 2000 quantization exponent count is invalid.");

            byte[] exponents = componentSpecific ? profile.QccExponents[component] : profile.QcdExponents;
            Array.Clear(exponents, 0, exponents.Length);
            for (int i = 0; i < count; i++)
            {
                byte exponent = reader.ReadByte();
                Require((exponent >> 3) != 0, "A JPEG 2000 quantization exponent is zero.");
                exponents[i] = exponent;
            }

            if (componentSpecific)
            {
                profile.QccCounts[component] = (byte)count;
                profile.QccGuardBits[component] = guardBits;
                profile.SawQcc = true;
            }
            else
            {
                profile.QcdCount = (byte)count;
                profile.QcdGuardBits = guardBits;
            }
        }

        private static void ParseNlt(ByteReader reader, Profile profile)
        {
            ushort length = reader.ReadUInt16BigEndian();
            Support(length == 6, "The JPEG 2000 NLT marker has an unsupported length.");
            int segmentEnd = reader.SegmentEnd(length);
            ushort component = reader.ReadUInt16BigEndian();
            byte precision = reader.ReadByte();
            byte type = reader.ReadByte();
            Support(type == 0 || type == 3, "The JPEG 2000 nonlinear transform is not supported.");
            Require(reader.Position == segmentEnd, "The JPEG 2000 NLT marker has unexpected trailing data.");

            Require(component == ushort.MaxValue || component < profile.ComponentCount,
                "The JPEG 2000 NLT component index is invalid.");
            int bitDepth = (precision & 0x7f) + 1;
            bool signed = (precision & 0x80) != 0;
            if (component == ushort.MaxValue)
            {
                for (int i = 0; i < profile.ComponentCount; i++)
                {
                    ValidateNltComponent(profile, i, bitDepth, signed, type);
                }
            }
            else
            {
                ValidateNltComponent(profile, component, bitDepth, signed, type);
            }
        }

        private static void ValidateNltComponent(Profile profile, int component, int bitDepth, bool signed, byte type)
        {
            int sizBitDepth = (profile.Ssiz[component] & 0x7f) + 1;
            bool sizSigned = (profile.Ssiz[component] & 0x80) != 0;
            Support(bitDepth == sizBitDepth && signed == sizSigned,
                "The JPEG 2000 NLT precision does not match SIZ.");
            Support(type != 3 || signed, "JPEG 2000 NLT type 3 requires signed samples.");
            profile.NltType[component] = type;
        }

        private static void ParseSot(ByteReader reader, Profile profile)
        {
            ushort length = reader.ReadUInt16BigEndian();
            Require(length == 10, "The JPEG 2000 SOT marker has an invalid length.");
            ushort tileIndex = reader.ReadUInt16BigEndian();
            uint tilePartLength = reader.ReadUInt32BigEndian();
            byte tilePartIndex = reader.ReadByte();
            byte tilePartCount = reader.ReadByte();
            Require(tileIndex == 0 && tilePartIndex == 0 && tilePartCount <= 1, "Only one JPEG 2000 tile-part is supported.");
            Require(tilePartLength == 0 || tilePartLength >= 14, "The JPEG 2000 tile-part length is invalid.");
            profile.Psot = tilePartLength;
            profile.SawSot = true;
        }

        private static void FinishTilePayload(ByteReader reader, Profile profile)
        {
            int payloadEnd;
            if (profile.Psot != 0)
            {
                long tilePartEnd = (long)profile.SotStart + profile.Psot;
                Require(tilePartEnd >= reader.Position && tilePartEnd <= reader.End,
                    "The JPEG 2000 tile-part extends outside the codestream.");
                payloadEnd = checked((int)tilePartEnd);
                Require(payloadEnd + 2 <= reader.End, "The JPEG 2000 codestream is missing EOC.");
                Require(ReadUInt16BigEndian(reader.Data, payloadEnd) == MarkerEoc,
                    "The JPEG 2000 tile-part is not followed by EOC.");
            }
            else
            {
                payloadEnd = FindEoc(reader.Data, reader.Position, reader.End);
            }

            Require(payloadEnd > reader.Position, "The JPEG 2000 tile-part has no packet data.");
            Require(payloadEnd + 2 == reader.End, "The JPEG 2000 codestream has trailing bytes after EOC.");
            profile.TileDataOffset = reader.Position;
            profile.TileDataLength = payloadEnd - reader.Position;
            reader.Position = payloadEnd + 2;
        }

        private static int FindEoc(byte[] data, int start, int end)
        {
            int position = start;
            while (position < end)
            {
                byte value = data[position++];
                if (value != 0xff)
                {
                    continue;
                }

                do
                {
                    Require(position < end, "The JPEG 2000 codestream is missing EOC.");
                    value = data[position++];
                }
                while (value == 0xff);

                if (value == 0)
                {
                    continue;
                }

                Require((ushort)(0xff00 | value) == MarkerEoc,
                    "An unexpected marker occurs inside JPEG 2000 packet data.");
                return position - 2;
            }

            throw new Htj2kDecodeException(Htj2kDecodeStatus.Corrupt, "The JPEG 2000 codestream is missing EOC.");
        }

        private static void ValidateProfile(DecodeContext context, Profile profile, ushort[] channelMap)
        {
            Require(profile.SawCod && profile.SawQcd && profile.SawSot && profile.SawSod,
                "The JPEG 2000 codestream is missing a required marker.");
            Require(profile.QcdCount == 1 + 3 * profile.NumDecompositions,
                "The JPEG 2000 QCD subband count is inconsistent with COD.");
            for (int component = 0; component < profile.ComponentCount; component++)
            {
                byte count = profile.QccCounts[component];
                Require(count == 0 || count == 1 + 3 * profile.NumDecompositions,
                    "A JPEG 2000 QCC subband count is inconsistent with COD.");
            }

            Require(profile.ComponentCount == context.Channels.Count,
                "The JPEG 2000 component count does not match the EXR channel count.");
            Support((profile.Rsiz & 0x4000) != 0, "The JPEG 2000 codestream is not an HT profile.");
            Support(profile.Xosiz == 0 && profile.Yosiz == 0 && profile.Xtosiz == 0 && profile.Ytosiz == 0,
                "Offset JPEG 2000 reference grids are not supported by the OpenEXR HTJ2K profile.");
            Require(profile.Xsiz <= context.Width && profile.Ysiz <= context.Height,
                "The JPEG 2000 tile dimensions exceed the EXR block region.");
            Support(profile.Xtsiz == profile.Xsiz && profile.Ytsiz == profile.Ysiz,
                "Multiple JPEG 2000 tiles are not supported inside one EXR block.");

            for (int component = 0; component < profile.ComponentCount; component++)
            {
                ValidateComponent(context, profile, channelMap, component);
            }
        }

        private static void ValidateComponent(
            DecodeContext context,
            Profile profile,
            ushort[] channelMap,
            int component)
        {
            int fileChannel = channelMap[component];
            Channel channel = context.Channels[fileChannel];
            int bitDepth = (profile.Ssiz[component] & 0x7f) + 1;
            bool signed = (profile.Ssiz[component] & 0x80) != 0;
            int expectedBits = channel.PixelType == PixelType.Half ? 16 : 32;
            bool expectedSigned = channel.PixelType != PixelType.UInt;
            Support(bitDepth == expectedBits && signed == expectedSigned,
                "A JPEG 2000 component precision does not match its EXR channel.");
            Require(profile.Xrsiz[component] != 0 && profile.Yrsiz[component] != 0,
                "A JPEG 2000 component has zero sampling.");
            Support(profile.Xrsiz[component] == channel.XSampling && profile.Yrsiz[component] == channel.YSampling,
                "A JPEG 2000 component sampling factor does not match its EXR channel.");

            Size componentSize = ComponentSize(profile, component);
            long expectedWidth = ModelValidation.CountSampleLocations(
                context.Region.MinX,
                context.Region.MaxX,
                channel.XSampling);
            long expectedHeight = ModelValidation.CountSampleLocations(
                context.Region.MinY,
                context.Region.MaxY,
                channel.YSampling);
            Require(componentSize.Width == expectedWidth && componentSize.Height == expectedHeight,
                "A JPEG 2000 component size does not match its EXR channel.");
        }

        private static ushort ReadUInt16BigEndian(byte[] data, int offset)
        {
            Require(offset >= 0 && offset <= data.Length - 2, "A big-endian 16-bit value is truncated.");
            return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            Require(offset >= 0 && offset <= data.Length - 4, "A big-endian 32-bit value is truncated.");
            return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
        }

        private static uint DivideCeiling(uint value, uint divisor)
        {
            Require(divisor != 0, "A JPEG 2000 divisor is zero.");
            return value / divisor + (value % divisor == 0 ? 0u : 1u);
        }

        private static Size ComponentSize(Profile profile, int component)
        {
            return new Size(
                DivideCeiling(profile.Xsiz, profile.Xrsiz[component]) -
                    DivideCeiling(profile.Xosiz, profile.Xrsiz[component]),
                DivideCeiling(profile.Ysiz, profile.Yrsiz[component]) -
                    DivideCeiling(profile.Yosiz, profile.Yrsiz[component]));
        }

        private static void DecodeTilePayload(
            DecodeContext context,
            Profile profile,
            ushort[] channelMap,
            byte[] source,
            Span<byte> destination)
        {
            // The packet parser, HT cleanup/SPP/MRP decoder, inverse 5/3 transform,
            // RCT/NLT, and canonical EXR row packing are implemented below.
            DecodePacketsAndStore(context, profile, channelMap, source, destination);
        }

        private static void DecodePacketsAndStore(
            DecodeContext context,
            Profile profile,
            ushort[] channelMap,
            byte[] source,
            Span<byte> destination)
        {
            List<CodeBlock> codeBlocks = ParseTilePackets(profile, source);
            Plane[] planes = AllocatePlanes(profile);
            try
            {
                CodeBlockDecodeWorkspace workspace = new CodeBlockDecodeWorkspace();
                foreach (CodeBlock codeBlock in codeBlocks)
                {
                    BandGeometry[] bands = BuildBandGeometries(
                        profile,
                        codeBlock.Component,
                        codeBlock.Resolution,
                        out _);
                    Require(codeBlock.Band >= 0 && codeBlock.Band < bands.Length && bands[codeBlock.Band].Exists,
                        "An HT codeblock references a nonexistent subband.");
                    long[] coefficients = DecodeCodeBlock(
                        source,
                        codeBlock,
                        bands[codeBlock.Band].Kmax,
                        workspace);
                    ScatterCodeBlock(profile, codeBlock, bands[codeBlock.Band], coefficients, planes[codeBlock.Component]);
                }

                PostprocessPlanes(profile, planes);
                StorePlanes(context, profile, channelMap, planes, destination);
            }
            finally
            {
                ReturnPlanes(planes);
            }
        }

        private static List<CodeBlock> ParseTilePackets(Profile profile, byte[] source)
        {
            Require(profile.NumDecompositions <= 31, "The JPEG 2000 decomposition count overflows precinct geometry.");
            int resolutionCount = profile.NumDecompositions + 1;
            PrecinctState[] states = new PrecinctState[checked(profile.ComponentCount * resolutionCount)];
            for (int component = 0; component < profile.ComponentCount; component++)
            {
                Size componentSize = ComponentSize(profile, component);
                for (int resolution = 0; resolution < resolutionCount; resolution++)
                {
                    Size size = ResolutionSize(componentSize, profile.NumDecompositions, (uint)resolution);
                    states[component * resolutionCount + resolution] = new PrecinctState(
                        DivideCeiling(size.Width, 1u << 15),
                        DivideCeiling(size.Height, 1u << 15));
                }
            }

            PacketBitReader reader = new PacketBitReader(
                source,
                profile.TileDataOffset,
                checked(profile.TileDataOffset + profile.TileDataLength));
            List<CodeBlock> result = new List<CodeBlock>();
            for (int resolution = 0; resolution < resolutionCount; resolution++)
            {
                while (true)
                {
                    int selectedComponent = -1;
                    ulong selectedX = ulong.MaxValue;
                    ulong selectedY = ulong.MaxValue;
                    for (int component = 0; component < profile.ComponentCount; component++)
                    {
                        PrecinctState state = states[component * resolutionCount + resolution];
                        if (!TryGetPrecinctTopLeft(
                            profile,
                            component,
                            resolution,
                            state,
                            out ulong x,
                            out ulong y))
                        {
                            continue;
                        }

                        if (selectedComponent < 0 || y < selectedY || (y == selectedY && x < selectedX))
                        {
                            selectedComponent = component;
                            selectedX = x;
                            selectedY = y;
                        }
                    }

                    if (selectedComponent < 0)
                    {
                        break;
                    }

                    int stateIndex = selectedComponent * resolutionCount + resolution;
                    PrecinctState selectedState = states[stateIndex];
                    ParsePrecinctPacket(
                        profile,
                        selectedComponent,
                        resolution,
                        selectedState.CurrentX,
                        selectedState.CurrentY,
                        reader,
                        result);
                    selectedState.Advance();
                    states[stateIndex] = selectedState;
                }
            }

            Require(reader.Position == reader.End, "The JPEG 2000 tile has unconsumed packet bytes.");
            return result;
        }

        private static bool TryGetPrecinctTopLeft(
            Profile profile,
            int component,
            int resolution,
            PrecinctState state,
            out ulong x,
            out ulong y)
        {
            if (state.CurrentY >= state.CountY || state.CurrentX >= state.CountX)
            {
                x = 0;
                y = 0;
                return false;
            }

            int shift = profile.NumDecompositions - resolution;
            ulong downX = (ulong)profile.Xrsiz[component] << shift;
            ulong downY = (ulong)profile.Yrsiz[component] << shift;
            x = checked(downX * ((ulong)state.CurrentX << 15));
            y = checked(downY * ((ulong)state.CurrentY << 15));
            return true;
        }

        private static void ParsePrecinctPacket(
            Profile profile,
            int component,
            int resolution,
            uint precinctX,
            uint precinctY,
            PacketBitReader reader,
            List<CodeBlock> destination)
        {
            BandGeometry[] bands = BuildBandGeometries(profile, component, resolution, out Size resolutionSize);
            List<CodeBlock> packetBlocks = new List<CodeBlock>();
            bool sawNonemptyBand = false;

            for (int bandIndex = 0; bandIndex < bands.Length; bandIndex++)
            {
                BandGeometry band = bands[bandIndex];
                if (!band.Exists)
                {
                    continue;
                }

                uint p0x = Math.Min(checked(precinctX << 15), resolutionSize.Width);
                uint p0y = Math.Min(checked(precinctY << 15), resolutionSize.Height);
                uint p1x = Math.Min(checked(p0x + (1u << 15)), resolutionSize.Width);
                uint p1y = Math.Min(checked(p0y + (1u << 15)), resolutionSize.Height);

                uint bandX0;
                uint bandX1;
                uint bandY0;
                uint bandY1;
                if (resolution == 0)
                {
                    bandX0 = p0x;
                    bandX1 = p1x;
                    bandY0 = p0y;
                    bandY1 = p1y;
                }
                else
                {
                    bandX0 = SubbandProject(p0x, (uint)bandIndex & 1u);
                    bandX1 = SubbandProject(p1x, (uint)bandIndex & 1u);
                    bandY0 = SubbandProject(p0y, (uint)bandIndex >> 1);
                    bandY1 = SubbandProject(p1y, (uint)bandIndex >> 1);
                }

                bandX1 = Math.Min(bandX1, band.Width);
                bandY1 = Math.Min(bandY1, band.Height);
                if (bandX1 <= bandX0 || bandY1 <= bandY0)
                {
                    continue;
                }

                uint codeBlockX0 = bandX0 / band.CodeBlockWidth;
                uint codeBlockX1 = DivideCeiling(bandX1, band.CodeBlockWidth);
                uint codeBlockY0 = bandY0 / band.CodeBlockHeight;
                uint codeBlockY1 = DivideCeiling(bandY1, band.CodeBlockHeight);
                uint codeBlockWidth = codeBlockX1 - codeBlockX0;
                uint codeBlockHeight = codeBlockY1 - codeBlockY0;
                if (codeBlockWidth == 0 || codeBlockHeight == 0)
                {
                    continue;
                }

                if (!sawNonemptyBand)
                {
                    if (reader.Read(1) == 0)
                    {
                        reader.TerminatePacket();
                        return;
                    }

                    sawNonemptyBand = true;
                }

                TagTree inclusionTree = new TagTree(codeBlockWidth, codeBlockHeight);
                TagTree missingMsbTree = new TagTree(codeBlockWidth, codeBlockHeight);
                for (uint y = 0; y < codeBlockHeight; y++)
                {
                    for (uint x = 0; x < codeBlockWidth; x++)
                    {
                        uint included = inclusionTree.Decode(reader, x, y, 1);
                        if (included >= 1)
                        {
                            continue;
                        }

                        uint missingMsbs = missingMsbTree.Decode(reader, x, y, checked(band.Kmax + 1));
                        Require(missingMsbs <= band.Kmax, "An HT codeblock has too many missing MSBs.");
                        ReadPassCount(reader, out uint activePasses, out uint placeholderGroups);
                        Require(placeholderGroups <= band.Kmax - missingMsbs,
                            "An HT codeblock placeholder pass count exceeds its bitplanes.");
                        ReadPassLengths(reader, activePasses, placeholderGroups, out uint length0, out uint length1);

                        uint globalX = checked(codeBlockX0 + x);
                        uint globalY = checked(codeBlockY0 + y);
                        ulong pixelX0 = (ulong)globalX * band.CodeBlockWidth;
                        ulong pixelY0 = (ulong)globalY * band.CodeBlockHeight;
                        ulong pixelX1 = Math.Min(pixelX0 + band.CodeBlockWidth, band.Width);
                        ulong pixelY1 = Math.Min(pixelY0 + band.CodeBlockHeight, band.Height);
                        Require(pixelX0 < band.Width && pixelY0 < band.Height &&
                            pixelX1 > pixelX0 && pixelY1 > pixelY0,
                            "An HT codeblock lies outside its subband.");

                        packetBlocks.Add(new CodeBlock(
                            component,
                            resolution,
                            bandIndex,
                            checked((uint)pixelX0),
                            checked((uint)pixelY0),
                            checked((uint)(pixelX1 - pixelX0)),
                            checked((uint)(pixelY1 - pixelY0)),
                            missingMsbs,
                            activePasses,
                            length0,
                            length1,
                            dataOffset: 0));
                    }
                }
            }

            if (!sawNonemptyBand)
            {
                reader.Read(1);
            }

            reader.TerminatePacket();
            foreach (CodeBlock metadata in packetBlocks)
            {
                int byteCount = checked((int)(metadata.Length0 + metadata.Length1));
                Require(byteCount <= reader.End - reader.Position, "An HT codeblock segment is truncated.");
                CodeBlock codeBlock = metadata.WithDataOffset(reader.Position);
                ValidateCodeBlockSegment(source: reader.Data, codeBlock);
                destination.Add(codeBlock);
                Require(destination.Count <= profile.TileDataLength / 2,
                    "The JPEG 2000 tile declares more codeblocks than its payload can contain.");
                reader.Position += byteCount;
            }
        }

        private static void ReadPassCount(
            PacketBitReader reader,
            out uint activePasses,
            out uint placeholderGroups)
        {
            uint passes = 1;
            if (reader.Read(1) != 0)
            {
                passes = 2;
                if (reader.Read(1) != 0)
                {
                    uint value = reader.Read(2);
                    passes = 3 + value;
                    if (value == 3)
                    {
                        value = reader.Read(5);
                        passes = 6 + value;
                        if (value == 31)
                        {
                            passes = 37 + reader.Read(7);
                        }
                    }
                }
            }

            placeholderGroups = (passes - 1) / 3;
            activePasses = passes - placeholderGroups * 3;
            Require(activePasses >= 1 && activePasses <= 3, "An HT codeblock pass count is invalid.");
        }

        private static void ReadPassLengths(
            PacketBitReader reader,
            uint activePasses,
            uint placeholderGroups,
            out uint length0,
            out uint length1)
        {
            Require(activePasses >= 1 && activePasses <= 3, "An HT codeblock active pass count is invalid.");
            uint lblock = 3;
            while (reader.Read(1) != 0)
            {
                lblock++;
                Require(lblock <= 32, "An HT codeblock Lblock value is too large.");
            }

            uint bits = checked(lblock + BitLength(placeholderGroups + 1) - 1);
            Require(bits <= 32, "An HT codeblock cleanup length field is too large.");
            length0 = reader.Read((int)bits);
            Require(length0 >= 2 && length0 < 65535, "An HT codeblock cleanup length is invalid.");

            length1 = 0;
            if (activePasses > 1)
            {
                bits = checked(lblock + (activePasses > 2 ? 1u : 0u));
                Require(bits <= 32, "An HT codeblock refinement length field is too large.");
                length1 = reader.Read((int)bits);
                Require(length1 < 2047, "An HT codeblock refinement length is invalid.");
            }
        }

        private static uint BitLength(uint value)
        {
            uint count = 0;
            while (value != 0)
            {
                count++;
                value >>= 1;
            }

            return count;
        }

        private static void ValidateCodeBlockSegment(byte[] source, CodeBlock codeBlock)
        {
            Require(codeBlock.Width >= 1 && codeBlock.Width <= 128 &&
                codeBlock.Height >= 1 && codeBlock.Height <= 32,
                "An HT codeblock has invalid dimensions.");
            Require(codeBlock.ActivePasses >= 1 && codeBlock.ActivePasses <= 3,
                "An HT codeblock has an invalid active pass count.");
            Require(codeBlock.ActivePasses == 1 || codeBlock.Length1 != 0,
                "An HT codeblock with refinement passes has no refinement segment.");
            Require(codeBlock.MissingMsbs <= 62, "An HT codeblock has too many missing MSBs.");
            Require(codeBlock.MissingMsbs < 30 || codeBlock.ActivePasses == 1,
                "An HT codeblock uses refinement outside the supported precision range.");

            CodeBlockStreams streams = SplitCodeBlockStreams(source, codeBlock);
            ValidateForwardStuffing(source, streams.MagSgnOffset, streams.MagSgnLength, 0x7f);
            ValidateForwardStuffing(source, streams.MelOffset, streams.MelLength, 0x8f);
            bool initialUnstuff = (source[codeBlock.DataOffset + checked((int)codeBlock.Length0) - 1] | 0x0f) > 0x8f;
            ValidateReverseStuffing(source, streams.VlcOffset, streams.VlcLength, initialUnstuff);
            if (streams.RefinementLength != 0)
            {
                ValidateForwardStuffing(source, streams.RefinementOffset, streams.RefinementLength, 0x7f);
                ValidateReverseStuffing(source, streams.RefinementOffset, streams.RefinementLength, initialUnstuff: true);
            }
        }

        private static CodeBlockStreams SplitCodeBlockStreams(byte[] source, CodeBlock codeBlock)
        {
            int length0 = checked((int)codeBlock.Length0);
            int length1 = checked((int)codeBlock.Length1);
            Require(length0 >= 2 && length0 < 65535 && length1 < 2047,
                "An HT codeblock segment length is invalid.");
            Require(codeBlock.DataOffset >= 0 && codeBlock.DataOffset <= source.Length - length0 - length1,
                "An HT codeblock segment is outside its payload.");

            int scup = (source[codeBlock.DataOffset + length0 - 1] << 4) |
                (source[codeBlock.DataOffset + length0 - 2] & 0x0f);
            Require(scup >= 2 && scup <= length0 && scup <= 4079,
                "An HT codeblock cleanup suffix length is invalid.");
            int suffixStart = checked(codeBlock.DataOffset + length0 - scup);
            return new CodeBlockStreams(
                codeBlock.DataOffset,
                suffixStart - codeBlock.DataOffset,
                suffixStart,
                scup - 1,
                suffixStart + 1,
                scup - 2,
                codeBlock.DataOffset + length0,
                length1,
                scup);
        }

        private static void ValidateForwardStuffing(byte[] source, int offset, int length, byte maximumAfterFf)
        {
            bool previousFf = false;
            for (int i = 0; i < length; i++)
            {
                byte value = source[offset + i];
                Require(!previousFf || value <= maximumAfterFf, "An HT forward bitstream has invalid stuffing.");
                previousFf = value == 0xff;
            }
        }

        private static void ValidateReverseStuffing(
            byte[] source,
            int offset,
            int length,
            bool initialUnstuff)
        {
            bool unstuff = initialUnstuff;
            for (int i = length; i > 0; i--)
            {
                byte value = source[offset + i - 1];
                Require(!unstuff || (value & 0x7f) != 0x7f || (value & 0x80) == 0,
                    "An HT reverse bitstream has invalid stuffing.");
                unstuff = value > 0x8f;
            }
        }

        private static BandGeometry[] BuildBandGeometries(
            Profile profile,
            int component,
            int resolution,
            out Size resolutionSize)
        {
            Size componentSize = ComponentSize(profile, component);
            resolutionSize = ResolutionSize(componentSize, profile.NumDecompositions, (uint)resolution);
            BandGeometry[] bands = new BandGeometry[4];
            if (resolution == 0)
            {
                bands[0] = new BandGeometry(
                    resolutionSize.Width,
                    resolutionSize.Height,
                    128,
                    32,
                    BandKmax(profile, component, 0, 0));
                return bands;
            }

            bands[1] = new BandGeometry(
                resolutionSize.Width >> 1,
                (resolutionSize.Height + 1) >> 1,
                128,
                32,
                BandKmax(profile, component, resolution, 1));
            bands[2] = new BandGeometry(
                (resolutionSize.Width + 1) >> 1,
                resolutionSize.Height >> 1,
                128,
                32,
                BandKmax(profile, component, resolution, 2));
            bands[3] = new BandGeometry(
                resolutionSize.Width >> 1,
                resolutionSize.Height >> 1,
                128,
                32,
                BandKmax(profile, component, resolution, 3));
            return bands;
        }

        private static uint BandKmax(Profile profile, int component, int resolution, int band)
        {
            byte count = profile.QccCounts[component];
            byte[] exponents;
            byte guardBits;
            if (count != 0)
            {
                exponents = profile.QccExponents[component];
                guardBits = profile.QccGuardBits[component];
            }
            else
            {
                count = profile.QcdCount;
                exponents = profile.QcdExponents;
                guardBits = profile.QcdGuardBits;
            }

            Require(count != 0, "The JPEG 2000 codestream has no quantization exponents.");
            int index = resolution != 0 ? (resolution - 1) * 3 + band : 0;
            if (index >= count)
            {
                index = count - 1;
            }

            uint exponent = (uint)exponents[index] >> 3;
            uint bits = exponent != 0 ? exponent - 1 : 0;
            return bits + guardBits;
        }

        private static Size ResolutionSize(Size componentSize, uint decompositions, uint resolution)
        {
            uint shift = decompositions - resolution;
            return new Size(
                DivideCeilingPowerOfTwo(componentSize.Width, shift),
                DivideCeilingPowerOfTwo(componentSize.Height, shift));
        }

        private static uint DivideCeilingPowerOfTwo(uint value, uint shift)
        {
            while (shift-- != 0)
            {
                value = (value + 1) >> 1;
            }

            return value;
        }

        private static uint SubbandProject(uint value, uint offset)
        {
            return value <= offset ? 0 : (value - offset + 1) >> 1;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new Htj2kDecodeException(Htj2kDecodeStatus.Corrupt, message);
            }
        }

        private static void Support(bool condition, string message)
        {
            if (!condition)
            {
                Unsupported(message);
            }
        }

        private static void Unsupported(string message)
        {
            throw new Htj2kDecodeException(Htj2kDecodeStatus.Unsupported, message);
        }

        private sealed class ByteReader
        {
            public ByteReader(byte[] data, int position, int end)
            {
                Require(position >= 0 && end >= position && end <= data.Length, "The JPEG 2000 reader bounds are invalid.");
                Data = data;
                Position = position;
                End = end;
            }

            public byte[] Data { get; }

            public int Position { get; set; }

            public int End { get; }

            public byte ReadByte()
            {
                Require(Position < End, "The JPEG 2000 segment is truncated.");
                return Data[Position++];
            }

            public ushort ReadUInt16BigEndian()
            {
                ushort value = Htj2kDecoder.ReadUInt16BigEndian(Data, Position);
                Require(Position <= End - 2, "The JPEG 2000 segment is truncated.");
                Position += 2;
                return value;
            }

            public uint ReadUInt32BigEndian()
            {
                uint value = Htj2kDecoder.ReadUInt32BigEndian(Data, Position);
                Require(Position <= End - 4, "The JPEG 2000 segment is truncated.");
                Position += 4;
                return value;
            }

            public int SegmentEnd(ushort length)
            {
                Require(length >= 2, "A JPEG 2000 marker segment length is invalid.");
                int segmentEnd = checked(Position + length - 2);
                Require(segmentEnd <= End, "A JPEG 2000 marker segment is truncated.");
                return segmentEnd;
            }

            public void SkipSegment()
            {
                ushort length = ReadUInt16BigEndian();
                Position = SegmentEnd(length);
            }

            public ushort NextMarker()
            {
                while (Position < End)
                {
                    byte value = ReadByte();
                    if (value != 0xff)
                    {
                        continue;
                    }

                    do
                    {
                        value = ReadByte();
                    }
                    while (value == 0xff);

                    if (value != 0)
                    {
                        return (ushort)(0xff00 | value);
                    }
                }

                throw new Htj2kDecodeException(Htj2kDecodeStatus.Corrupt, "The JPEG 2000 codestream ended before the next marker.");
            }
        }

        private sealed class PacketBitReader
        {
            private ulong _bits;
            private int _bitCount;
            private bool _previousFf;

            public PacketBitReader(byte[] data, int position, int end)
            {
                Require(position >= 0 && end >= position && end <= data.Length,
                    "The JPEG 2000 packet reader bounds are invalid.");
                Data = data;
                Position = position;
                End = end;
            }

            public byte[] Data { get; }

            public int Position { get; set; }

            public int End { get; }

            public uint Read(int bitCount)
            {
                Require(bitCount >= 0 && bitCount <= 32, "A JPEG 2000 packet read width is invalid.");
                while (_bitCount < bitCount)
                {
                    Require(Position < End, "A JPEG 2000 packet header is truncated.");
                    byte value = Data[Position++];
                    int addedBits = 8;
                    if (_previousFf)
                    {
                        Require((value & 0x80) == 0, "A JPEG 2000 packet header has invalid bit stuffing.");
                        value &= 0x7f;
                        addedBits = 7;
                    }

                    Require(_bitCount + addedBits <= 64, "A JPEG 2000 packet bit accumulator overflowed.");
                    _bits = (_bits << addedBits) | value;
                    _bitCount += addedBits;
                    _previousFf = addedBits == 8 && value == 0xff;
                }

                if (bitCount == 0)
                {
                    return 0;
                }

                ulong mask = bitCount == 32 ? uint.MaxValue : (1UL << bitCount) - 1;
                uint result = (uint)((_bits >> (_bitCount - bitCount)) & mask);
                _bitCount -= bitCount;
                _bits = _bitCount == 0 ? 0 : _bits & ((1UL << _bitCount) - 1);
                return result;
            }

            public void TerminatePacket()
            {
                int drop = _bitCount & 7;
                if (drop != 0)
                {
                    _bitCount -= drop;
                    _bits = _bitCount == 0 ? 0 : _bits & ((1UL << _bitCount) - 1);
                }

                if (_previousFf)
                {
                    Require(Position < End, "A JPEG 2000 packet header is missing its stuffing byte.");
                    byte value = Data[Position++];
                    Require((value & 0x80) == 0, "A JPEG 2000 packet terminator has invalid stuffing.");
                    _previousFf = false;
                }

                _bits = 0;
                _bitCount = 0;
            }
        }

        private sealed class TagTree
        {
            private const int MaximumLevels = 32;

            private readonly uint[] _widths;
            private readonly uint[] _heights;
            private readonly int[] _offsets;
            private readonly uint[] _values;
            private readonly bool[] _known;

            public TagTree(uint width, uint height)
            {
                Require(width != 0 && height != 0, "A JPEG 2000 tag-tree has zero dimensions.");
                List<uint> widths = new List<uint>();
                List<uint> heights = new List<uint>();
                List<int> offsets = new List<int>();
                long total = 0;
                uint currentWidth = width;
                uint currentHeight = height;
                while (true)
                {
                    Require(widths.Count < MaximumLevels, "A JPEG 2000 tag-tree has too many levels.");
                    widths.Add(currentWidth);
                    heights.Add(currentHeight);
                    offsets.Add(checked((int)total));
                    total = checked(total + (long)currentWidth * currentHeight);
                    Require(total <= int.MaxValue, "A JPEG 2000 tag-tree is too large for managed storage.");
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
                _values = new uint[checked((int)total)];
                _known = new bool[_values.Length];
            }

            public uint Decode(PacketBitReader reader, uint leafX, uint leafY, uint threshold)
            {
                Require(leafX < _widths[0] && leafY < _heights[0],
                    "A JPEG 2000 tag-tree leaf is outside the tree.");
                uint[] xs = new uint[_widths.Length];
                uint[] ys = new uint[_widths.Length];
                xs[0] = leafX;
                ys[0] = leafY;
                for (int level = 1; level < _widths.Length; level++)
                {
                    xs[level] = xs[level - 1] >> 1;
                    ys[level] = ys[level - 1] >> 1;
                }

                uint low = 0;
                for (int level = _widths.Length - 1; level >= 0; level--)
                {
                    int index = checked(_offsets[level] + (int)(ys[level] * _widths[level] + xs[level]));
                    Require(index >= 0 && index < _values.Length, "A JPEG 2000 tag-tree node is invalid.");
                    if (_values[index] < low)
                    {
                        _values[index] = low;
                    }

                    while (_values[index] < threshold && !_known[index])
                    {
                        if (reader.Read(1) != 0)
                        {
                            _known[index] = true;
                        }
                        else
                        {
                            _values[index]++;
                        }
                    }

                    low = _values[index];
                }

                return low;
            }
        }

        private sealed class DecodeContext
        {
            public DecodeContext(Header header, Box2i region)
            {
                Header = header;
                Region = region;
                Width = checked((uint)region.Width);
                Height = checked((uint)region.Height);
            }

            public Header Header { get; }

            public Box2i Region { get; }

            public IReadOnlyList<Channel> Channels => Header.Channels;

            public uint Width { get; }

            public uint Height { get; }
        }

        private sealed class Profile
        {
            public uint Xsiz;
            public uint Ysiz;
            public uint Xosiz;
            public uint Yosiz;
            public uint Xtsiz;
            public uint Ytsiz;
            public uint Xtosiz;
            public uint Ytosiz;
            public uint Psot;
            public int SotStart;
            public int TileDataOffset;
            public int TileDataLength;
            public ushort Rsiz;
            public byte[] Ssiz = Array.Empty<byte>();
            public byte[] Xrsiz = Array.Empty<byte>();
            public byte[] Yrsiz = Array.Empty<byte>();
            public byte[] NltType = Array.Empty<byte>();
            public byte[] QcdExponents = new byte[16];
            public byte QcdCount;
            public byte QcdGuardBits;
            public byte[][] QccExponents = Array.Empty<byte[]>();
            public byte[] QccCounts = Array.Empty<byte>();
            public byte[] QccGuardBits = Array.Empty<byte>();
            public byte NumDecompositions;
            public byte MultipleComponentTransform;
            public bool SawSiz;
            public bool SawCap;
            public bool SawCod;
            public bool SawQcd;
            public bool SawSot;
            public bool SawSod;
            public bool SawQcc;

            public int ComponentCount => Ssiz.Length;
        }

        private readonly struct Size
        {
            public Size(uint width, uint height)
            {
                Width = width;
                Height = height;
            }

            public uint Width { get; }

            public uint Height { get; }
        }

        private readonly struct BandGeometry
        {
            public BandGeometry(uint width, uint height, uint codeBlockWidth, uint codeBlockHeight, uint kmax)
            {
                Width = width;
                Height = height;
                CodeBlockWidth = codeBlockWidth;
                CodeBlockHeight = codeBlockHeight;
                Kmax = kmax;
            }

            public uint Width { get; }

            public uint Height { get; }

            public uint CodeBlockWidth { get; }

            public uint CodeBlockHeight { get; }

            public uint Kmax { get; }

            public bool Exists => Width != 0 && Height != 0;
        }

        private struct PrecinctState
        {
            public PrecinctState(uint countX, uint countY)
            {
                CountX = countX;
                CountY = countY;
                CurrentX = 0;
                CurrentY = 0;
            }

            public uint CountX { get; }

            public uint CountY { get; }

            public uint CurrentX { get; private set; }

            public uint CurrentY { get; private set; }

            public void Advance()
            {
                CurrentX++;
                if (CurrentX >= CountX)
                {
                    CurrentX = 0;
                    CurrentY++;
                }
            }
        }

        private readonly struct CodeBlock
        {
            public CodeBlock(
                int component,
                int resolution,
                int band,
                uint x,
                uint y,
                uint width,
                uint height,
                uint missingMsbs,
                uint activePasses,
                uint length0,
                uint length1,
                int dataOffset)
            {
                Component = component;
                Resolution = resolution;
                Band = band;
                X = x;
                Y = y;
                Width = width;
                Height = height;
                MissingMsbs = missingMsbs;
                ActivePasses = activePasses;
                Length0 = length0;
                Length1 = length1;
                DataOffset = dataOffset;
            }

            public int Component { get; }

            public int Resolution { get; }

            public int Band { get; }

            public uint X { get; }

            public uint Y { get; }

            public uint Width { get; }

            public uint Height { get; }

            public uint MissingMsbs { get; }

            public uint ActivePasses { get; }

            public uint Length0 { get; }

            public uint Length1 { get; }

            public int DataOffset { get; }

            public CodeBlock WithDataOffset(int dataOffset)
            {
                return new CodeBlock(
                    Component,
                    Resolution,
                    Band,
                    X,
                    Y,
                    Width,
                    Height,
                    MissingMsbs,
                    ActivePasses,
                    Length0,
                    Length1,
                    dataOffset);
            }
        }

        private readonly struct CodeBlockStreams
        {
            public CodeBlockStreams(
                int magSgnOffset,
                int magSgnLength,
                int melOffset,
                int melLength,
                int vlcOffset,
                int vlcLength,
                int refinementOffset,
                int refinementLength,
                int scup)
            {
                MagSgnOffset = magSgnOffset;
                MagSgnLength = magSgnLength;
                MelOffset = melOffset;
                MelLength = melLength;
                VlcOffset = vlcOffset;
                VlcLength = vlcLength;
                RefinementOffset = refinementOffset;
                RefinementLength = refinementLength;
                Scup = scup;
            }

            public int MagSgnOffset { get; }

            public int MagSgnLength { get; }

            public int MelOffset { get; }

            public int MelLength { get; }

            public int VlcOffset { get; }

            public int VlcLength { get; }

            public int RefinementOffset { get; }

            public int RefinementLength { get; }

            public int Scup { get; }
        }

        private sealed class Htj2kDecodeException : Exception
        {
            public Htj2kDecodeException(Htj2kDecodeStatus status, string message)
                : base(message)
            {
                Status = status;
            }

            public Htj2kDecodeStatus Status { get; }
        }
    }

    internal enum Htj2kDecodeStatus
    {
        Success,
        Corrupt,
        Unsupported,
    }
}
