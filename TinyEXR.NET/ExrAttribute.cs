using System;
using System.Buffers.Binary;
using System.Text;

namespace TinyEXR
{
    public sealed class ExrAttribute
    {
        public ExrAttribute(string name, string typeName, byte[] value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name { get; }

        public string TypeName { get; }

        public byte[] Value { get; }

        public static ExrAttribute FromString(string name, string value)
        {
            return new ExrAttribute(name, "string", Encoding.UTF8.GetBytes(value + "\0"));
        }

        public static ExrAttribute FromInt(string name, int value)
        {
            byte[] data = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(data, value);
            return new ExrAttribute(name, "int", data);
        }

        public static ExrAttribute FromFloat(string name, float value)
        {
            byte[] data = new byte[sizeof(float)];
            BinaryPrimitives.WriteInt32LittleEndian(data, BitConverter.SingleToInt32Bits(value));
            return new ExrAttribute(name, "float", data);
        }

        public static ExrAttribute FromDouble(string name, double value)
        {
            byte[] data = new byte[sizeof(double)];
            BinaryPrimitives.WriteInt64LittleEndian(data, BitConverter.DoubleToInt64Bits(value));
            return new ExrAttribute(name, "double", data);
        }

        public byte ReadByte(int byteOffset = 0)
        {
            return ReadValue(byteOffset, sizeof(byte))[0];
        }

        public int ReadInt(int byteOffset = 0)
        {
            return ReadInt32(byteOffset);
        }

        public int ReadInt32(int byteOffset = 0)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(ReadValue(byteOffset, sizeof(int)));
        }

        public uint ReadUInt(int byteOffset = 0)
        {
            return ReadUInt32(byteOffset);
        }

        public uint ReadUInt32(int byteOffset = 0)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(ReadValue(byteOffset, sizeof(uint)));
        }

        public long ReadInt64(int byteOffset = 0)
        {
            return BinaryPrimitives.ReadInt64LittleEndian(ReadValue(byteOffset, sizeof(long)));
        }

        public ulong ReadUInt64(int byteOffset = 0)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(ReadValue(byteOffset, sizeof(ulong)));
        }

        public float ReadFloat(int byteOffset = 0)
        {
            return ReadSingle(byteOffset);
        }

        public float ReadSingle(int byteOffset = 0)
        {
            return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(ReadValue(byteOffset, sizeof(float))));
        }

        public double ReadDouble(int byteOffset = 0)
        {
            return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(ReadValue(byteOffset, sizeof(double))));
        }

        public string ReadString()
        {
            int count = Value.Length;
            if (count > 0 && Value[count - 1] == 0)
            {
                count--;
            }

            return Encoding.UTF8.GetString(Value, 0, count);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            return ReadValue(byteOffset, byteCount);
        }

        public string? GetStringValue()
        {
            if (!string.Equals(TypeName, "string", StringComparison.Ordinal))
            {
                return null;
            }

            return ReadString();
        }

        public int? GetInt32Value()
        {
            if (!string.Equals(TypeName, "int", StringComparison.Ordinal) || Value.Length < sizeof(int))
            {
                return null;
            }

            return ReadInt32();
        }

        public float? GetFloatValue()
        {
            return GetSingleValue();
        }

        public float? GetSingleValue()
        {
            if (!string.Equals(TypeName, "float", StringComparison.Ordinal) || Value.Length < sizeof(float))
            {
                return null;
            }

            return ReadSingle();
        }

        public double? GetDoubleValue()
        {
            if (!string.Equals(TypeName, "double", StringComparison.Ordinal) || Value.Length < sizeof(double))
            {
                return null;
            }

            return ReadDouble();
        }

        private ReadOnlySpan<byte> ReadValue(int byteOffset, int byteCount)
        {
            if (byteOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "The byte offset must be non-negative.");
            }

            if (byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "The byte count must be non-negative.");
            }

            if (byteOffset > Value.Length || byteCount > Value.Length - byteOffset)
            {
                throw new InvalidOperationException($"Attribute '{Name}' value is {Value.Length} bytes, but reading {byteCount} bytes at offset {byteOffset} would exceed it.");
            }

            return Value.AsSpan(byteOffset, byteCount);
        }
    }
}
