namespace TinyEXR
{
    public unsafe partial struct DeepImage
    {
        [NativeTypeName("const char **")]
        public sbyte** channel_names;

        public float*** image;

        public int** offset_table;

        public int num_channels;

        public int width;

        public int height;

        public int pad0;
    }
}