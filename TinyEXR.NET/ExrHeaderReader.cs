using System;
using System.Numerics;
using System.Text;

namespace TinyEXR
{
    public class ExrHeaderReader : IDisposable
    {
        internal Native.EXRHeader _impl;
        bool _disposedValue;

        public float PixelAspectRatio => _impl.pixel_aspect_ratio;
        public ExrBox2Int DataWindow => new ExrBox2Int(_impl.data_window);
        public ExrBox2Int DisplayWindow => new ExrBox2Int(_impl.display_window);
        public CompressionType Compression => (CompressionType)_impl.compression_type;
        public LineOrderType LineOrder => (LineOrderType)_impl.line_order;
        public Vector2 ScreenWindowCenter
        {
            get
            {
                unsafe
                {
                    fixed (float* ptr = _impl.screen_window_center)
                    {
                        return new Vector2(ptr[0], ptr[1]);
                    }
                }
            }
        }
        public float ScreenWindowWidth => _impl.screen_window_width;
        public bool IsMultipart => _impl.multipart != 0;
        public int ChannelCount => _impl.num_channels;
        public ExrChannelInfo[] Channels
        {
            get
            {
                ExrChannelInfo[] info = new ExrChannelInfo[_impl.num_channels];
                unsafe
                {
                    Native.EXRChannelInfo* infoPtr = (Native.EXRChannelInfo*)_impl.channels.ToPointer();
                    for (int i = 0; i < info.Length; i++)
                    {
                        info[i] = new ExrChannelInfo()
                        {
                            Name = Encoding.UTF8.GetString((byte*)infoPtr[i].name, (int)Exr.StrLen((byte*)infoPtr[i].name)),
                            Type = (ExrPixelType)infoPtr[i].pixel_type,
                            SamplingX = infoPtr[i].x_sampling,
                            SamplingY = infoPtr[i].y_sampling,
                            Linear = infoPtr[i].p_linear
                        };
                    }
                }
                return info;
            }
        }

        public ExrHeaderReader(string path)
        {
            if (Exr.ParseVersionFromFile(path, out var version) != ResultCode.Success)
            {
                throw new TinyExrException($"can not open {path}");
            }
            if (Exr.ParseHeaderFromFile(path, version, out _impl) != ResultCode.Success)
            {
                throw new TinyExrException($"can not open {path}");
            }
        }

        public ExrHeaderReader(string path, ExrVersion version)
        {
            if (Exr.ParseHeaderFromFile(path, version, out _impl) != ResultCode.Success)
            {
                throw new TinyExrException($"can not open {path}");
            }
        }

        public ExrHeaderReader(ReadOnlySpan<byte> data)
        {
            if (Exr.ParseVersionFromMemory(data, out var version) != ResultCode.Success)
            {
                throw new TinyExrException($"can not parse version");
            }
            if (Exr.ParseHeaderFromMemory(data, version, out _impl) != ResultCode.Success)
            {
                throw new TinyExrException($"can not parse header");
            }
        }

        public ExrHeaderReader(ReadOnlySpan<byte> data, ExrVersion version)
        {
            if (Exr.ParseHeaderFromMemory(data, version, out _impl) != ResultCode.Success)
            {
                throw new TinyExrException($"can not parse header");
            }
        }

        public ExrChannelInfo GetChannelInfo(int channel)
        {
            if (channel < 0 || channel >= ChannelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
            int i = channel;
            unsafe
            {
                Native.EXRChannelInfo* infoPtr = (Native.EXRChannelInfo*)_impl.channels.ToPointer();
                ExrChannelInfo info = new ExrChannelInfo()
                {
                    Name = Encoding.UTF8.GetString((byte*)infoPtr[i].name, (int)Exr.StrLen((byte*)infoPtr[i].name)),
                    Type = (ExrPixelType)infoPtr[i].pixel_type,
                    SamplingX = infoPtr[i].x_sampling,
                    SamplingY = infoPtr[i].y_sampling,
                    Linear = infoPtr[i].p_linear
                };
                return info;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                }

                Exr.FreeHeader(in _impl);
                _impl = default;

                _disposedValue = true;
            }
        }

        ~ExrHeaderReader()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
