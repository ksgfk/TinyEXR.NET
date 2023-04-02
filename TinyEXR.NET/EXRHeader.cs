using System.Numerics;
using TinyEXR.Native;

namespace TinyEXR
{
    public struct ExrHeader
    {
        internal EXRHeader _header;

        public float PixelAspectRatio => _header.pixel_aspect_ratio;
        public LineOrderType LineOrder => (LineOrderType)_header.line_order;
        public ExrBox2i DataWindow => new ExrBox2i() { _box = _header.data_window };
        public ExrBox2i DisplayWindow => new ExrBox2i() { _box = _header.display_window };
        public Vector2 ScreenWindowCenter
        {
            get
            {
                unsafe
                {
                    return new Vector2(_header.screen_window_center[0], _header.screen_window_center[1]);
                }
            }
        }
        public float ScreenWindowWidth => _header.screen_window_width;
        public CompressionType Compression => (CompressionType)_header.compression_type;
        public ExrChannel[] Channels
        {
            get
            {
                unsafe
                {
                    ExrChannel[] channels = new ExrChannel[_header.num_channels];
                    for (int i = 0; i < channels.Length; i++)
                    {
                        channels[i] = new ExrChannel() { _channel = _header.channels[i] };
                    }
                    return channels;
                }
            }
        }
    }
}
