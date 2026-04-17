using System;
using System.Text;
using TinyEXR.Native;

namespace TinyEXR
{
    internal static class NativeUtf8
    {
        public static byte[] ToNullTerminated(string value)
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            byte[] buffer = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
            return buffer;
        }

        public static unsafe string Read(sbyte* ptr)
        {
            if (ptr == null)
            {
                return string.Empty;
            }

            ulong size = EXRNative.StrLenInternal(ptr).ToUInt64();
            if (size >= int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(ptr));
            }

            return Encoding.UTF8.GetString((byte*)ptr, (int)size);
        }

        public static void WriteNullTerminated(string value, Span<byte> destination)
        {
            if (destination.Length == 0)
            {
                throw new ArgumentException("Destination buffer cannot be empty.", nameof(destination));
            }

            destination.Clear();

            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount >= destination.Length)
            {
                throw new ArgumentException("Destination buffer is too small for the UTF-8 string.", nameof(destination));
            }

            Encoding.UTF8.GetBytes(value, destination[..byteCount]);
        }
    }
}
