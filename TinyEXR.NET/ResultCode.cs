using TinyEXR.Native;

namespace TinyEXR
{
    public enum ResultCode
    {
        Success = EXRNative.TINYEXR_SUCCESS,
        InvalidMagicNumver = EXRNative.TINYEXR_ERROR_INVALID_MAGIC_NUMBER,
        InvalidExrVersion = EXRNative.TINYEXR_ERROR_INVALID_EXR_VERSION,
        InvalidArgument = EXRNative.TINYEXR_ERROR_INVALID_ARGUMENT,
        InvalidData = EXRNative.TINYEXR_ERROR_INVALID_DATA,
        InvalidFile = EXRNative.TINYEXR_ERROR_INVALID_FILE,
        InvalidParameter = EXRNative.TINYEXR_ERROR_INVALID_PARAMETER,
        CannotOpenFile = EXRNative.TINYEXR_ERROR_CANT_OPEN_FILE,
        UnsupportedFormat = EXRNative.TINYEXR_ERROR_UNSUPPORTED_FORMAT,
        InvalidHeader = EXRNative.TINYEXR_ERROR_INVALID_HEADER,
        UnsupportedFeature = EXRNative.TINYEXR_ERROR_UNSUPPORTED_FEATURE,
        CannotWriteFile = EXRNative.TINYEXR_ERROR_CANT_WRITE_FILE,
        SerialzationFailed = EXRNative.TINYEXR_ERROR_SERIALIZATION_FAILED,
        LayerNotFound = EXRNative.TINYEXR_ERROR_LAYER_NOT_FOUND,
        DataTooLarge = EXRNative.TINYEXR_ERROR_DATA_TOO_LARGE
    }
}
