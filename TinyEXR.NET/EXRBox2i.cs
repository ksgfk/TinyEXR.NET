using TinyEXR.Native;

namespace TinyEXR
{
    public struct ExrBox2i
    {
        internal EXRBox2i _box;

        public int MinX => _box.min_x;
        public int MinY => _box.min_y;
        public int MaxX => _box.max_x;
        public int MaxY => _box.max_y;
    }
}
