using System.Text;
using TinyEXR.Native;

namespace TinyEXR
{
    public struct ExrChannel
    {
        internal EXRChannelInfo _channel;

        public string Name
        {
            get
            {
                unsafe
                {
                    fixed (sbyte* ptr = _channel.name)
                    {
                        return Encoding.UTF8.GetString((byte*)ptr, (int)EXRNative.StrLenInternal(ptr).ToUInt64());
                    }
                }
            }
        }
        public PixelType PixelType => (PixelType)_channel.pixel_type;
        public byte PLinear => _channel.p_linear;
        public int XSampling => _channel.x_sampling;
        public int YSampling => _channel.y_sampling;
    }
}
