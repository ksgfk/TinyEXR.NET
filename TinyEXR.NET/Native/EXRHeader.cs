namespace TinyEXR.Native
{
    public unsafe partial struct EXRHeader
    {
        public float pixel_aspect_ratio;

        public int line_order;

        public EXRBox2i data_window;

        public EXRBox2i display_window;

        [NativeTypeName("float[2]")]
        public fixed float screen_window_center[2];

        public float screen_window_width;

        public int chunk_count;

        public int tiled;

        public int tile_size_x;

        public int tile_size_y;

        public int tile_level_mode;

        public int tile_rounding_mode;

        public int long_name;

        public int non_image;

        public int multipart;

        [NativeTypeName("unsigned int")]
        public uint header_len;

        public int num_custom_attributes;

        public EXRAttribute* custom_attributes;

        public EXRChannelInfo* channels;

        public int* pixel_types;

        public int num_channels;

        public int compression_type;

        public int* requested_pixel_types;

        [NativeTypeName("char[256]")]
        public fixed sbyte name[256];
    }
}