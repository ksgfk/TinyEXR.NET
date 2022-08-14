namespace TinyEXR
{
    public struct ExrBox2Int
    {
        internal Native.EXRBox2i _impl;

        public int MinX { get => _impl.min_x; set => _impl.min_x = value; }
        public int MaxX { get => _impl.max_x; set => _impl.max_x = value; }
        public int MinY { get => _impl.min_y; set => _impl.min_y = value; }
        public int MaxY { get => _impl.max_y; set => _impl.max_y = value; }

        internal ExrBox2Int(Native.EXRBox2i box) { _impl = box; }

        public ExrBox2Int(int minX, int minY, int maxX, int maxY)
        {
            _impl.min_x = minX;
            _impl.min_y = minY;
            _impl.max_x = maxX;
            _impl.max_y = maxY;
        }
    }
}
