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

        public string? GetStringValue()
        {
            if (!string.Equals(TypeName, "string", StringComparison.Ordinal))
            {
                return null;
            }

            int count = Value.Length;
            if (count > 0 && Value[count - 1] == 0)
            {
                count--;
            }

            return Encoding.UTF8.GetString(Value, 0, count);
        }

        public int? GetInt32Value()
        {
            if (!string.Equals(TypeName, "int", StringComparison.Ordinal) || Value.Length < sizeof(int))
            {
                return null;
            }

            return BinaryPrimitives.ReadInt32LittleEndian(Value);
        }
    }
}
