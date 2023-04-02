namespace TinyEXR.Native
{
    public unsafe partial struct EXRChannelInfo
    {
        [NativeTypeName("char[256]")]
        public fixed sbyte name[256];

        public int pixel_type;

        public int x_sampling;

        public int y_sampling;

        [NativeTypeName("unsigned char")]
        public byte p_linear;

        [NativeTypeName("unsigned char[3]")]
        public fixed byte pad[3];
    }
}
