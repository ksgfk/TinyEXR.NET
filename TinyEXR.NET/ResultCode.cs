namespace TinyEXR
{
    public enum ResultCode
    {
        Success = 0,
        InvalidMagicNumver = -1,
        InvalidExrVersion = -2,
        InvalidArgument = -3,
        InvalidData = -4,
        InvalidFile = -5,
        InvalidParameter = -6,
        CannotOpenFile = -7,
        UnsupportedFormat = -8,
        InvalidHeader = -9,
        UnsupportedFeature = -10,
        CannotWriteFile = -11,
        SerialzationFailed = -12,
        LayerNotFound = -13,
        DataTooLarge = -14
    }
}
