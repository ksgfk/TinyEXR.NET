using TinyEXR.Native;

namespace TinyEXR
{
    public struct ExrImage
    {
        internal EXRImage _img;

        public int LevelX => _img.level_x;
        public int LevelY => _img.level_y;
        public int Width => _img.width;
        public int Height => _img.height;
    }
}
