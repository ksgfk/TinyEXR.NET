namespace TinyEXR.Native
{
    public unsafe partial struct EXRTile
    {
        public int offset_x;

        public int offset_y;

        public int level_x;

        public int level_y;

        public int width;

        public int height;

        [NativeTypeName("unsigned char **")]
        public byte** images;
    }
}