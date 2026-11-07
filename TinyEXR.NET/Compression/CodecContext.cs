// Port of tinyexr v3 exr_codec_ctx / sampled block layout, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    internal readonly struct CodecContext
    {
        public CodecContext(IReadOnlyList<Channel> channels, Box2i region)
        {
            Channels = channels;
            Region = region;
        }

        public IReadOnlyList<Channel> Channels { get; }
        public Box2i Region { get; }
        public int Width(int channel) => checked((int)ModelValidation.CountSampleLocations(
            Region.MinX, Region.MaxX, Channels[channel].XSampling));
        public int Height(int channel) => checked((int)ModelValidation.CountSampleLocations(
            Region.MinY, Region.MaxY, Channels[channel].YSampling));
        public int SampleSize(int channel) => ModelValidation.PixelTypeSize(Channels[channel].PixelType);

        public int ByteCount
        {
            get
            {
                int count = 0;
                for (int c = 0; c < Channels.Count; c++)
                {
                    count = checked(count + checked(Width(c) * Height(c) * SampleSize(c)));
                }
                return count;
            }
        }

        // Convert the canonical scanline/channel layout to consecutive channel planes and back.
        public void Reorder(ReadOnlySpan<byte> source, Span<byte> destination, bool toPlanar)
        {
            int[] positions = new int[Channels.Count];
            int planeStart = 0;
            for (int c = 0; c < positions.Length; c++)
            {
                positions[c] = planeStart;
                planeStart = checked(planeStart + checked(Width(c) * Height(c) * SampleSize(c)));
            }
            if (source.Length != planeStart || destination.Length != planeStart)
            {
                throw new ArgumentException("The canonical block size does not match its channel layout.");
            }
            int offset = 0;
            for (long y = Region.MinY; y <= Region.MaxY; y++)
            {
                for (int c = 0; c < Channels.Count; c++)
                {
                    if (y % Channels[c].YSampling != 0)
                        continue;
                    int bytes = checked(Width(c) * SampleSize(c));
                    if (toPlanar)
                        source.Slice(offset, bytes).CopyTo(destination.Slice(positions[c], bytes));
                    else
                        source.Slice(positions[c], bytes).CopyTo(destination.Slice(offset, bytes));
                    positions[c] += bytes;
                    offset += bytes;
                }
            }
        }
    }
}
