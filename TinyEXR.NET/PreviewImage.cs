using System.Buffers.Binary;

namespace TinyEXR
{
    /// <summary>Immutable, unpremultiplied RGBA8 preview pixels in row order.</summary>
    public sealed class PreviewImage
    {
        public const int MaximumPixelCount = 1 << 24;
        private readonly byte[] _rgba8;

        public PreviewImage(int width, int height, ReadOnlySpan<byte> rgba8)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            long pixels = (long)width * height;
            if (pixels > MaximumPixelCount) throw new ArgumentOutOfRangeException(nameof(width));
            if (rgba8.Length != pixels * 4) throw new ArgumentException("Preview pixels must contain width * height RGBA8 values.", nameof(rgba8));
            Width = width;
            Height = height;
            _rgba8 = rgba8.ToArray();
        }

        public int Width { get; }
        public int Height { get; }
        public ReadOnlySpan<byte> Rgba8 => _rgba8;

        internal byte[] Encode()
        {
            byte[] result = new byte[8 + _rgba8.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)Width);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)Height);
            _rgba8.CopyTo(result, 8);
            return result;
        }

        internal static bool IsValid(ReadOnlySpan<byte> data)
        {
            if (data.Length < 8) return false;
            uint width = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint height = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
            ulong pixels = (ulong)width * height;
            return width > 0 && height > 0 && pixels <= MaximumPixelCount && (ulong)data.Length == 8 + pixels * 4;
        }
    }
}
