using System;
using TinyEXR.Native;

namespace TinyEXR
{
    public class SinglePartExrReader
    {
        byte[] _image = null!;
        int[] _offset = null!;
        int[] _length = null!;
        ExrChannel[] _channels = null!;

        public ExrChannel[] Channels => _channels;

        public void Read(string path)
        {
            ResultCode result = Exr.ParseEXRVersionFromFile(path, out EXRVersion version);
            if (result != ResultCode.Success)
            {
                throw new ArgumentException($"cannot parse version, result: {result}, file: {path}");
            }
            if (version.multipart != 0)
            {
                throw new ArgumentException($"{path} is multi-part");
            }
            EXRHeader header = default;
            EXRImage image = default;
            try
            {
                Exr.InitEXRHeader(ref header);
                Exr.InitEXRImage(ref image);
                result = Exr.ParseEXRHeaderFromFile(path, ref version, ref header);
                if (result != ResultCode.Success)
                {
                    throw new ArgumentException($"cannot parse header, result: {result}, file: {path}");
                }
                result = Exr.LoadEXRImageFromFile(ref image, ref header, path);
                if (result != ResultCode.Success)
                {
                    throw new ArgumentException($"cannot parse image, result: {result}, file: {path}");
                }
                ProcessImage(ref header, ref image);
            }
            finally
            {
                Exr.FreeEXRHeader(ref header);
                Exr.FreeEXRImage(ref image);
            }
        }

        public void Read(ReadOnlySpan<byte> data)
        {
            ResultCode result = Exr.ParseEXRVersionFromMemory(data, out EXRVersion version);
            if (result != ResultCode.Success)
            {
                throw new ArgumentException($"cannot parse version, result: {result}");
            }
            if (version.multipart != 0)
            {
                throw new ArgumentException($"this is multi-part data");
            }
            EXRHeader header = default;
            EXRImage image = default;
            try
            {
                Exr.InitEXRHeader(ref header);
                Exr.InitEXRImage(ref image);
                result = Exr.ParseEXRHeaderFromMemory(data, ref version, ref header);
                if (result != ResultCode.Success)
                {
                    throw new ArgumentException($"cannot parse header, result: {result}");
                }
                result = Exr.LoadEXRImageFromMemory(ref image, ref header, data);
                if (result != ResultCode.Success)
                {
                    throw new ArgumentException($"cannot parse image, result: {result}");
                }
                ProcessImage(ref header, ref image);
            }
            finally
            {
                Exr.FreeEXRHeader(ref header);
                Exr.FreeEXRImage(ref image);
            }
        }

        private void ProcessImage(ref EXRHeader header, ref EXRImage image)
        {
            _channels = new ExrChannel[header.num_channels];
            _offset = new int[header.num_channels];
            _length = new int[header.num_channels];
            int dataSize = 0;
            for (int i = 0; i < _channels.Length; i++)
            {
                unsafe
                {
                    ref EXRChannelInfo ch = ref header.channels[i];
                    _channels[i] = new ExrChannel(
                        Exr.ReadExrChannelInfoName(ref ch),
                        (ExrPixelType)ch.pixel_type,
                        ch.x_sampling,
                        ch.y_sampling,
                        ch.p_linear);
                }
                _offset[i] = dataSize;
                _length[i] = image.width * image.height * Exr.TypeSize(_channels[i].Type);
                dataSize += _length[i];
            }
            _image = new byte[dataSize];
            unsafe
            {
                if (image.tiles != null && image.images == null)
                {
                    for (int i = 0; i < image.num_tiles; i++)
                    {
                        ref EXRTile tile = ref image.tiles[i];
                        for (int j = 0; j < _offset.Length; j++)
                        {
                            int typeSize = Exr.TypeSize(_channels[i].Type);
                            int length = tile.width * tile.height * typeSize;
                            for (int y = 0; y < tile.height; y++)
                            {
                                for (int x = 0; x < tile.width; x++)
                                {
                                    int from = (y * tile.width + x) * typeSize;
                                    ReadOnlySpan<byte> src = new ReadOnlySpan<byte>(tile.images[j] + from, typeSize);
                                    int to = ((y + tile.offset_y) * image.width + (x + tile.offset_x)) * typeSize;
                                    Span<byte> dst = new Span<byte>(_image, to, typeSize);
                                    src.CopyTo(dst);
                                }
                            }
                        }
                    }
                }
                else if (image.tiles == null && image.images != null)
                {
                    for (int i = 0; i < _offset.Length; i++)
                    {
                        int length = _length[i];
                        ReadOnlySpan<byte> src = new ReadOnlySpan<byte>(image.images[i], length);
                        Span<byte> dst = new Span<byte>(_image, _offset[i], length);
                        src.CopyTo(dst);
                    }
                }
                else
                {
                    throw new ArgumentException($"internal error");
                }
            }
        }

        public ReadOnlySpan<byte> GetImageData(int channel)
        {
            if (channel < 0 || channel >= _channels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
            return new ReadOnlySpan<byte>(_image, _offset[channel], _length[channel]);
        }

        public ReadOnlySpan<byte> GetImageData(string channelName)
        {
            for (int i = 0; i < _channels.Length; i++)
            {
                ExrChannel ch = _channels[i];
                if (ch.Name == channelName)
                {
                    return GetImageData(i);
                }
            }
            throw new ArgumentOutOfRangeException(nameof(channelName));
        }
    }
}
