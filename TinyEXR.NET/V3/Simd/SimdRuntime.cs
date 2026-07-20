using System;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace TinyEXR.V3
{
    /// <summary>
    /// Runtime SIMD capabilities available to managed TinyEXR v3 code.
    /// Values match the public TinyEXR v3 <c>exr_simd_caps</c> bits.
    /// </summary>
    [Flags]
    public enum SimdCapabilities : uint
    {
        None = 0,
        Sse2 = 1U << 0,
        Sse41 = 1U << 1,
        Avx2 = 1U << 2,
        Neon = 1U << 3,
    }

    /// <summary>
    /// Read-only runtime SIMD diagnostics.
    /// </summary>
    public static class SimdRuntime
    {
        private static readonly SimdCapabilities DetectedCapabilities = DetectCapabilities();
        private static readonly bool DetectedF16c = DetectF16c();
        private static readonly string DetectedInfo = GetInfo(DetectedCapabilities, DetectedF16c);

        /// <summary>
        /// Gets all SIMD instruction-set tiers exposed by the current runtime.
        /// The netstandard2.1 build always reports <see cref="SimdCapabilities.None"/>.
        /// </summary>
        public static SimdCapabilities Capabilities => DetectedCapabilities;

        /// <summary>
        /// Gets the highest available tier as <c>scalar</c>, <c>sse2</c>,
        /// <c>sse4.1</c>, <c>avx2</c>, <c>avx2+f16c</c>, or <c>neon</c>.
        /// </summary>
        public static string Info => DetectedInfo;

#if NET8_0_OR_GREATER
        internal static bool IntrinsicsCompiled => true;
#else
        internal static bool IntrinsicsCompiled => false;
#endif

        private static SimdCapabilities DetectCapabilities()
        {
#if NET8_0_OR_GREATER
            SimdCapabilities capabilities = SimdCapabilities.None;
            if (Sse2.IsSupported)
            {
                capabilities |= SimdCapabilities.Sse2;
            }

            if (Sse41.IsSupported)
            {
                capabilities |= SimdCapabilities.Sse41;
            }

            if (Avx2.IsSupported)
            {
                capabilities |= SimdCapabilities.Avx2;
            }

            if (AdvSimd.IsSupported)
            {
                capabilities |= SimdCapabilities.Neon;
            }

            return capabilities;
#else
            return SimdCapabilities.None;
#endif
        }

        private static bool DetectF16c()
        {
#if NET8_0_OR_GREATER
            if (!X86Base.IsSupported || !Avx.IsSupported)
            {
                return false;
            }

            (int _, int _, int ecx, int _) = X86Base.CpuId(1, 0);
            return (ecx & (1 << 29)) != 0;
#else
            return false;
#endif
        }

        private static string GetInfo(SimdCapabilities capabilities, bool hasF16c)
        {
            if ((capabilities & SimdCapabilities.Neon) != 0)
            {
                return "neon";
            }

            if ((capabilities & SimdCapabilities.Avx2) != 0)
            {
                return hasF16c ? "avx2+f16c" : "avx2";
            }

            if ((capabilities & SimdCapabilities.Sse41) != 0)
            {
                return "sse4.1";
            }

            if ((capabilities & SimdCapabilities.Sse2) != 0)
            {
                return "sse2";
            }

            return "scalar";
        }
    }
}
