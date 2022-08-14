namespace TinyEXR
{
    public class ExrChannelInfo
    {
        public string Name { get; set; } = string.Empty;
        public ExrPixelType Type { get; set; }
        public int SamplingX { get; set; }
        public int SamplingY { get; set; }
        public byte Linear { get; set; }
    }
}
