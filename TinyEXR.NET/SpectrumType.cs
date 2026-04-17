using TinyEXR.Native;

namespace TinyEXR
{
    public enum SpectrumType
    {
        Reflective = EXRNative.TINYEXR_SPECTRUM_REFLECTIVE,
        Emissive = EXRNative.TINYEXR_SPECTRUM_EMISSIVE,
        Polarised = EXRNative.TINYEXR_SPECTRUM_POLARISED,
    }
}
