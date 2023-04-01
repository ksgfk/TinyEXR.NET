namespace TinyEXR
{
    public unsafe partial struct EXRAttribute
    {
        [NativeTypeName("char[256]")]
        public fixed sbyte name[256];

        [NativeTypeName("char[256]")]
        public fixed sbyte type[256];

        [NativeTypeName("unsigned char *")]
        public byte* value;

        public int size;

        public int pad0;
    }
}