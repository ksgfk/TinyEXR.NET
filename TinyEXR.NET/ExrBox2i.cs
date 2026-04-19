namespace TinyEXR
{
    public readonly struct ExrBox2i
    {
        public ExrBox2i(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public int MinX { get; }

        public int MinY { get; }

        public int MaxX { get; }

        public int MaxY { get; }

        public int Width => MaxX - MinX + 1;

        public int Height => MaxY - MinY + 1;
    }
}
