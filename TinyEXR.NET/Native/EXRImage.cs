namespace TinyEXR.Native
{
    public unsafe partial struct EXRImage
    {
        public EXRTile* tiles;

        [NativeTypeName("struct TEXRImage *")]
        public EXRImage* next_level;

        public int level_x;

        public int level_y;

        [NativeTypeName("unsigned char **")]
        public byte** images;

        public int width;

        public int height;

        public int num_channels;

        public int num_tiles;
    }
}