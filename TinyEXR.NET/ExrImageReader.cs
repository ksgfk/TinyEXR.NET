using System;

namespace TinyEXR
{
    public class ExrImageReader : IDisposable
    {
        readonly ExrHeaderReader _header;
        internal Native.EXRImage _impl;
        bool _disposedValue;

        public ExrHeaderReader Header => _header;
        public int Width => _impl.width;
        public int Height => _impl.height;
        public int ChannelCount => _impl.num_channels;
        public unsafe bool IsTiled => _impl.images.ToPointer() == null && _impl.tiles.ToPointer() != null;
        public unsafe bool IsScanline => _impl.images.ToPointer() != null && _impl.tiles.ToPointer() == null;

        public ExrImageReader(string file)
        {
            _header = new ExrHeaderReader(file);
            if (Exr.ParseImageFromFile(file, in _header._impl, out _impl) != ResultCode.Success)
            {
                throw new TinyExrException($"can not open {file}");
            }
        }

        public ExrImageReader(ReadOnlySpan<byte> data)
        {
            _header = new ExrHeaderReader(data);
            if (Exr.ParseImageFromMemory(data, in _header._impl, out _impl) != ResultCode.Success)
            {
                throw new TinyExrException($"can not parse image");
            }
        }

        public ReadOnlySpan<byte> GetPixels(int channel)
        {
            if (channel < 0 || channel >= ChannelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
            unsafe
            {
                byte** images = (byte**)_impl.images.ToPointer();
                return new ReadOnlySpan<byte>(images[channel], Width * Height);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                }

                _header.Dispose();
                Exr.FreeImage(in _impl);
                _impl = default;

                _disposedValue = true;
            }
        }

        ~ExrImageReader()
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
