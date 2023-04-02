using TinyEXR.Native;

namespace TinyEXR
{
    public enum CompressionType
    {
        None = EXRNative.TINYEXR_COMPRESSIONTYPE_NONE,
        RLE = EXRNative.TINYEXR_COMPRESSIONTYPE_RLE,
        ZIPS = EXRNative.TINYEXR_COMPRESSIONTYPE_ZIPS,
        ZIP = EXRNative.TINYEXR_COMPRESSIONTYPE_ZIP,
        PIZ = EXRNative.TINYEXR_COMPRESSIONTYPE_PIZ,
        PXR24 = 5,
        B44 = 6,
        B44A = 7
    }
}
