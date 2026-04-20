using System;

namespace TinyEXR
{
    public class ExrChannel
    {
        public string Name { get; } = string.Empty;
        public ExrPixelType Type { get; }
        public ExrPixelType RequestedPixelType { get; set; }
        public int SamplingX { get; }
        public int SamplingY { get; }
        public byte Linear { get; }

        public ExrChannel(string name, ExrPixelType type, int samplingX, int samplingY, byte linear)
            : this(name, type, type, samplingX, samplingY, linear)
        {
        }

        public ExrChannel(string name, ExrPixelType type, ExrPixelType requestedPixelType, int samplingX, int samplingY, byte linear)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Type = type;
            RequestedPixelType = requestedPixelType;
            SamplingX = samplingX;
            SamplingY = samplingY;
            Linear = linear;
        }

        public ExrChannel(string name, ExrPixelType type) : this(name, type, 1, 1, 1) { }
    }
}
