using System;

namespace TinyEXR
{
    public class ExrChannel
    {
        public string Name { get; } = string.Empty;
        public ExrPixelType Type { get; }
        public int SamplingX { get; }
        public int SamplingY { get; }
        public byte Linear { get; }

        public ExrChannel(string name, ExrPixelType type, int samplingX, int samplingY, byte linear)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            SamplingX = samplingX;
            SamplingY = samplingY;
            Linear = linear;
        }
    }
}
