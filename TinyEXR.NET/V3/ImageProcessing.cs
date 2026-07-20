using System;
using System.Numerics;

namespace TinyEXR.V3
{
    /// <summary>
    /// Row-major linear RGB transformation matrix.
    /// </summary>
    public readonly struct ColorMatrix3x3
    {
        public ColorMatrix3x3(
            float m11,
            float m12,
            float m13,
            float m21,
            float m22,
            float m23,
            float m31,
            float m32,
            float m33)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M31 = m31;
            M32 = m32;
            M33 = m33;
        }

        public float M11 { get; }

        public float M12 { get; }

        public float M13 { get; }

        public float M21 { get; }

        public float M22 { get; }

        public float M23 { get; }

        public float M31 { get; }

        public float M32 { get; }

        public float M33 { get; }

        public static ColorMatrix3x3 Identity { get; } = new ColorMatrix3x3(
            1.0f, 0.0f, 0.0f,
            0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, 1.0f);

        public float this[int row, int column]
        {
            get
            {
                if ((uint)row >= 3U)
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }

                if ((uint)column >= 3U)
                {
                    throw new ArgumentOutOfRangeException(nameof(column));
                }

                switch ((row * 3) + column)
                {
                    case 0: return M11;
                    case 1: return M12;
                    case 2: return M13;
                    case 3: return M21;
                    case 4: return M22;
                    case 5: return M23;
                    case 6: return M31;
                    case 7: return M32;
                    default: return M33;
                }
            }
        }

        public void CopyTo(Span<float> destination)
        {
            if (destination.Length < 9)
            {
                throw new ArgumentException("The destination must contain at least nine elements.", nameof(destination));
            }

            destination[0] = M11;
            destination[1] = M12;
            destination[2] = M13;
            destination[3] = M21;
            destination[4] = M22;
            destination[5] = M23;
            destination[6] = M31;
            destination[7] = M32;
            destination[8] = M33;
        }
    }

    /// <summary>
    /// Parameters used by the TinyEXR v3 tone-map operators. Zero values select
    /// the same defaults as the native v3 API.
    /// </summary>
    public readonly struct ToneMapParameters
    {
        public ToneMapParameters(
            float exposure = 0.0f,
            float whitePoint = 0.0f,
            float a = 0.0f,
            float b = 0.0f,
            float c = 0.0f,
            float d = 0.0f,
            float e = 0.0f,
            float f = 0.0f,
            float w = 0.0f)
        {
            Exposure = exposure;
            WhitePoint = whitePoint;
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
            W = w;
        }

        public float Exposure { get; }

        public float WhitePoint { get; }

        public float A { get; }

        public float B { get; }

        public float C { get; }

        public float D { get; }

        public float E { get; }

        public float F { get; }

        public float W { get; }

        internal bool HasDefaultHableCurve =>
            A == 0.0f && B == 0.0f && C == 0.0f && D == 0.0f &&
            E == 0.0f && F == 0.0f && W == 0.0f;
    }

    /// <summary>
    /// Float image-processing utilities published by TinyEXR v3.
    /// </summary>
    public static class ImageProcessing
    {
        private static readonly int[] WhitePointIds = { 0, 0, 1, 1, 0 };

        private static readonly float[][] WhitePoints =
        {
            new[] { 0.95047f, 1.0f, 1.08883f },
            new[] { 0.952646f, 1.0f, 1.008825f },
        };

        private static readonly ColorMatrix3x3[] RgbToXyz =
        {
            new ColorMatrix3x3(
                0.4123907993f, 0.3575843394f, 0.1804807884f,
                0.2126390059f, 0.7151686788f, 0.0721923154f,
                0.0193308187f, 0.1191947798f, 0.9505321522f),
            new ColorMatrix3x3(
                0.6369580483f, 0.1446169036f, 0.1688809752f,
                0.2627002120f, 0.6779980715f, 0.0593017165f,
                0.0f, 0.0280726930f, 1.0609850577f),
            new ColorMatrix3x3(
                0.9525523959f, 0.0f, 0.0000936786f,
                0.3439664498f, 0.7281660966f, -0.0721325464f,
                0.0f, 0.0f, 1.0088251844f),
            new ColorMatrix3x3(
                0.6624541811f, 0.1340042065f, 0.1561876870f,
                0.2722287168f, 0.6740817658f, 0.0536895174f,
                -0.0055746495f, 0.0040607335f, 1.0103391003f),
            ColorMatrix3x3.Identity,
        };

        private static readonly ColorMatrix3x3[] XyzToRgb =
        {
            new ColorMatrix3x3(
                3.2409699419f, -1.5373831776f, -0.4986107603f,
                -0.9692436363f, 1.8759675015f, 0.0415550574f,
                0.0556300797f, -0.2039769589f, 1.0569715142f),
            new ColorMatrix3x3(
                1.7166511880f, -0.3556707838f, -0.2533662814f,
                -0.6666843518f, 1.6164812366f, 0.0157685458f,
                0.0176398574f, -0.0427706133f, 0.9421031212f),
            new ColorMatrix3x3(
                1.0498110175f, 0.0f, -0.0000974845f,
                -0.4959030231f, 1.3733130458f, 0.0982400361f,
                0.0f, 0.0f, 0.9912520182f),
            new ColorMatrix3x3(
                1.6410233797f, -0.3248032942f, -0.2364246952f,
                -0.6636628587f, 1.6153315917f, 0.0167563477f,
                0.0117218943f, -0.0082844420f, 0.9883948585f),
            ColorMatrix3x3.Identity,
        };

        private static readonly ColorMatrix3x3 Bradford = new ColorMatrix3x3(
            0.8951000f, 0.2664000f, -0.1614000f,
            -0.7502000f, 1.7135000f, 0.0367000f,
            0.0389000f, -0.0685000f, 1.0296000f);

        private static readonly ColorMatrix3x3 BradfordInverse = new ColorMatrix3x3(
            0.9869929f, -0.1470543f, 0.1599627f,
            0.4323053f, 0.5183603f, 0.0492912f,
            -0.0085287f, 0.0400428f, 0.9684867f);

        public static void Resize(
            ReadOnlySpan<float> source,
            int sourceWidth,
            int sourceHeight,
            Span<float> destination,
            int destinationWidth,
            int destinationHeight,
            int channels,
            ResizeFilter filter = ResizeFilter.Mitchell,
            EdgeMode edgeMode = EdgeMode.Clamp,
            int alphaChannel = -1,
            int sourceRowStride = 0,
            int destinationRowStride = 0)
        {
            ValidateDimensions(sourceWidth, sourceHeight, destinationWidth, destinationHeight, channels);
            ModelValidation.ValidateEnum(filter, nameof(filter));
            ModelValidation.ValidateEnum(edgeMode, nameof(edgeMode));
            if (alphaChannel < -1 || alphaChannel >= channels)
            {
                throw new ArgumentOutOfRangeException(nameof(alphaChannel));
            }

            int tightSource = checked(sourceWidth * channels);
            int tightDestination = checked(destinationWidth * channels);
            int effectiveSourceStride = sourceRowStride == 0 ? tightSource : sourceRowStride;
            int effectiveDestinationStride = destinationRowStride == 0 ? tightDestination : destinationRowStride;
            if (effectiveSourceStride < tightSource)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRowStride));
            }

            if (effectiveDestinationStride < tightDestination)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationRowStride));
            }

            int requiredSource = checked(((sourceHeight - 1) * effectiveSourceStride) + tightSource);
            int requiredDestination = checked(((destinationHeight - 1) * effectiveDestinationStride) + tightDestination);
            if (source.Length < requiredSource)
            {
                throw new ArgumentException("The source does not contain every requested row.", nameof(source));
            }

            if (destination.Length < requiredDestination)
            {
                throw new ArgumentException("The destination does not contain every requested row.", nameof(destination));
            }

            if (source.Slice(0, requiredSource).Overlaps(destination.Slice(0, requiredDestination)))
            {
                throw new ArgumentException("Resize source and destination storage must not overlap.", nameof(destination));
            }

            using (StreamingImageResizer resizer = new StreamingImageResizer(
                sourceWidth,
                sourceHeight,
                destinationWidth,
                destinationHeight,
                channels,
                PixelType.Float,
                filter,
                edgeMode))
            {
                float[] row = new float[Math.Max(tightSource, tightDestination)];
                int nextSourceY = 0;
                while (true)
                {
                    ExrResult result = resizer.PullRow(row.AsSpan(0, tightDestination), out int destinationY);
                    if (result == ExrResult.WouldBlock)
                    {
                        if (nextSourceY >= sourceHeight)
                        {
                            throw new InvalidOperationException("The resize contributor table requested a row past the source image.");
                        }

                        ReadOnlySpan<float> sourceRow = source.Slice(
                            nextSourceY * effectiveSourceStride,
                            tightSource);
                        if (alphaChannel >= 0)
                        {
                            for (int x = 0; x < sourceWidth; x++)
                            {
                                int offset = x * channels;
                                float alpha = sourceRow[offset + alphaChannel];
                                for (int channel = 0; channel < channels; channel++)
                                {
                                    row[offset + channel] = channel == alphaChannel
                                        ? alpha
                                        : sourceRow[offset + channel] * alpha;
                                }
                            }

                            resizer.PushRow(nextSourceY, row.AsSpan(0, tightSource));
                        }
                        else
                        {
                            resizer.PushRow(nextSourceY, sourceRow);
                        }

                        nextSourceY++;
                        continue;
                    }

                    if (destinationY >= destinationHeight)
                    {
                        return;
                    }

                    Span<float> destinationRow = destination.Slice(
                        destinationY * effectiveDestinationStride,
                        tightDestination);
                    if (alphaChannel >= 0)
                    {
                        for (int x = 0; x < destinationWidth; x++)
                        {
                            int offset = x * channels;
                            float alpha = row[offset + alphaChannel];
                            float inverseAlpha = alpha != 0.0f ? 1.0f / alpha : 0.0f;
                            for (int channel = 0; channel < channels; channel++)
                            {
                                destinationRow[offset + channel] = channel == alphaChannel
                                    ? alpha
                                    : row[offset + channel] * inverseAlpha;
                            }
                        }
                    }
                    else
                    {
                        row.AsSpan(0, tightDestination).CopyTo(destinationRow);
                    }
                }
            }
        }

        public static void ToneMap(
            ReadOnlySpan<float> source,
            Span<float> destination,
            int channels,
            ToneMapOperator operation,
            ToneMapParameters? parameters = null)
        {
            int elementCount = ValidateInterleaved(source, destination, channels);
            ModelValidation.ValidateEnum(operation, nameof(operation));
            ValidateAliasing(source, destination, elementCount);

            ToneMapParameters settings = parameters ?? default;
            float exposure = settings.Exposure != 0.0f ? settings.Exposure : 1.0f;
            float whitePoint = settings.WhitePoint != 0.0f ? settings.WhitePoint : 1.0f;
            float whitePointSquared = whitePoint * whitePoint;
            if (operation == ToneMapOperator.Hable && settings.HasDefaultHableCurve)
            {
                settings = new ToneMapParameters(
                    exposure,
                    whitePoint,
                    0.15f,
                    0.50f,
                    0.10f,
                    0.20f,
                    0.02f,
                    0.30f,
                    11.2f);
            }

            float hableNormalization = 1.0f;
            if (operation == ToneMapOperator.Hable)
            {
                hableNormalization = HableCurve(settings.W, settings);
                if (hableNormalization == 0.0f)
                {
                    hableNormalization = 1.0f;
                }
            }

            int colorChannels = Math.Min(channels, 3);
            int pixelCount = elementCount / channels;
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int offset = pixel * channels;
                for (int channel = 0; channel < colorChannels; channel++)
                {
                    float value = source[offset + channel] * exposure;
                    switch (operation)
                    {
                        case ToneMapOperator.Reinhard:
                            value /= 1.0f + value;
                            break;
                        case ToneMapOperator.ReinhardExtended:
                            value = value * (1.0f + (value / whitePointSquared)) / (1.0f + value);
                            break;
                        case ToneMapOperator.Aces:
                            value = AcesNarkowicz(value);
                            break;
                        case ToneMapOperator.Hable:
                            value = HableCurve(value, settings) / hableNormalization;
                            break;
                    }

                    destination[offset + channel] = value;
                }

                for (int channel = colorChannels; channel < channels; channel++)
                {
                    destination[offset + channel] = source[offset + channel];
                }
            }
        }

        public static ColorMatrix3x3 GetColorMatrix(ColorSpace from, ColorSpace to)
        {
            ModelValidation.ValidateEnum(from, nameof(from));
            ModelValidation.ValidateEnum(to, nameof(to));
            if (from == to)
            {
                return ColorMatrix3x3.Identity;
            }

            int fromIndex = (int)from;
            int toIndex = (int)to;
            if (WhitePointIds[fromIndex] == WhitePointIds[toIndex])
            {
                return Multiply(XyzToRgb[toIndex], RgbToXyz[fromIndex]);
            }

            ColorMatrix3x3 adaptation = GetAdaptationMatrix(
                WhitePoints[WhitePointIds[fromIndex]],
                WhitePoints[WhitePointIds[toIndex]]);
            return Multiply(XyzToRgb[toIndex], Multiply(adaptation, RgbToXyz[fromIndex]));
        }

        public static Vector3 GetLuminanceWeights(Chromaticities? chromaticities)
        {
            if (!chromaticities.HasValue)
            {
                return new Vector3(0.2126f, 0.7152f, 0.0722f);
            }

            Chromaticities value = chromaticities.Value;
            float denominator =
                (value.RedX * (value.BlueY - value.GreenY)) +
                (value.BlueX * (value.GreenY - value.RedY)) +
                (value.GreenX * (value.RedY - value.BlueY));
            if (value.WhiteY == 0.0f || denominator == 0.0f)
            {
                return new Vector3(0.2126f, 0.7152f, 0.0722f);
            }

            float whiteX = value.WhiteX / value.WhiteY;
            float whiteZ = (1.0f - value.WhiteX - value.WhiteY) / value.WhiteY;
            float redScale =
                (whiteX * (value.BlueY - value.GreenY) -
                 value.GreenX * ((value.BlueY - 1.0f) + value.BlueY * (whiteX + whiteZ)) +
                 value.BlueX * ((value.GreenY - 1.0f) + value.GreenY * (whiteX + whiteZ))) /
                denominator;
            float greenScale =
                (whiteX * (value.RedY - value.BlueY) +
                 value.RedX * ((value.BlueY - 1.0f) + value.BlueY * (whiteX + whiteZ)) -
                 value.BlueX * ((value.RedY - 1.0f) + value.RedY * (whiteX + whiteZ))) /
                denominator;
            float blueScale =
                (whiteX * (value.GreenY - value.RedY) -
                 value.RedX * ((value.GreenY - 1.0f) + value.GreenY * (whiteX + whiteZ)) +
                 value.GreenX * ((value.RedY - 1.0f) + value.RedY * (whiteX + whiteZ))) /
                denominator;

            Vector3 result = new Vector3(
                redScale * value.RedY,
                greenScale * value.GreenY,
                blueScale * value.BlueY);
            float sum = result.X + result.Y + result.Z;
            return sum == 0.0f ? result : result / sum;
        }

        public static void ApplyColorMatrix(
            ReadOnlySpan<float> source,
            Span<float> destination,
            int channels,
            ColorMatrix3x3 matrix)
        {
            ApplyColorMatrix(source, destination, channels, matrix, Vector.IsHardwareAccelerated);
        }

        internal static void ApplyColorMatrix(
            ReadOnlySpan<float> source,
            Span<float> destination,
            int channels,
            ColorMatrix3x3 matrix,
            bool vectorized)
        {
            int elementCount = ValidateInterleaved(source, destination, channels);
            ValidateAliasing(source, destination, elementCount);
            if (channels < 3)
            {
                source.Slice(0, elementCount).CopyTo(destination);
                return;
            }

            int pixelCount = elementCount / channels;
            Vector3 redColumn = new Vector3(matrix.M11, matrix.M21, matrix.M31);
            Vector3 greenColumn = new Vector3(matrix.M12, matrix.M22, matrix.M32);
            Vector3 blueColumn = new Vector3(matrix.M13, matrix.M23, matrix.M33);
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int offset = pixel * channels;
                float red = source[offset];
                float green = source[offset + 1];
                float blue = source[offset + 2];
                if (vectorized)
                {
                    Vector3 transformed = ((redColumn * red) + (greenColumn * green)) + (blueColumn * blue);
                    destination[offset] = transformed.X;
                    destination[offset + 1] = transformed.Y;
                    destination[offset + 2] = transformed.Z;
                }
                else
                {
                    destination[offset] = (matrix.M11 * red) + (matrix.M12 * green) + (matrix.M13 * blue);
                    destination[offset + 1] = (matrix.M21 * red) + (matrix.M22 * green) + (matrix.M23 * blue);
                    destination[offset + 2] = (matrix.M31 * red) + (matrix.M32 * green) + (matrix.M33 * blue);
                }

                if (channels == 4)
                {
                    destination[offset + 3] = source[offset + 3];
                }
            }
        }

        public static void EncodeTransfer(
            ReadOnlySpan<float> source,
            Span<float> destination,
            TransferFunction transferFunction)
        {
            ApplyTransfer(source, destination, transferFunction, encode: true);
        }

        public static void DecodeTransfer(
            ReadOnlySpan<float> source,
            Span<float> destination,
            TransferFunction transferFunction)
        {
            ApplyTransfer(source, destination, transferFunction, encode: false);
        }

        private static void ApplyTransfer(
            ReadOnlySpan<float> source,
            Span<float> destination,
            TransferFunction transferFunction,
            bool encode)
        {
            ModelValidation.ValidateEnum(transferFunction, nameof(transferFunction));
            if (destination.Length < source.Length)
            {
                throw new ArgumentException("The destination must be at least as long as the source.", nameof(destination));
            }

            ValidateAliasing(source, destination, source.Length);
            if (transferFunction == TransferFunction.Linear)
            {
                source.CopyTo(destination);
                return;
            }

            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = encode
                    ? EncodeTransferValue(source[index], transferFunction)
                    : DecodeTransferValue(source[index], transferFunction);
            }
        }

        private static float EncodeTransferValue(float value, TransferFunction transferFunction)
        {
            switch (transferFunction)
            {
                case TransferFunction.Srgb:
                    value = NonNegative(value);
                    return value <= 0.0031308f
                        ? 12.92f * value
                        : (1.055f * MathF.Pow(value, 1.0f / 2.4f)) - 0.055f;
                case TransferFunction.Gamma22:
                    return MathF.Pow(NonNegative(value), 1.0f / 2.2f);
                case TransferFunction.Gamma24:
                    return MathF.Pow(NonNegative(value), 1.0f / 2.4f);
                case TransferFunction.Rec709:
                    value = NonNegative(value);
                    return value < 0.018f
                        ? 4.5f * value
                        : (1.099f * MathF.Pow(value, 0.45f)) - 0.099f;
                case TransferFunction.Pq:
                    value = NonNegative(value);
                    float powered = MathF.Pow(value, 0.1593017578125f);
                    return MathF.Pow(
                        (0.8359375f + (18.8515625f * powered)) /
                        (1.0f + (18.6875f * powered)),
                        78.84375f);
                case TransferFunction.Hlg:
                    value = NonNegative(value);
                    return value <= (1.0f / 12.0f)
                        ? MathF.Sqrt(3.0f * value)
                        : (0.17883277f * MathF.Log((12.0f * value) - 0.28466892f)) + 0.55991073f;
                default:
                    return value;
            }
        }

        private static float DecodeTransferValue(float value, TransferFunction transferFunction)
        {
            switch (transferFunction)
            {
                case TransferFunction.Srgb:
                    return value <= 0.04045f
                        ? value / 12.92f
                        : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
                case TransferFunction.Gamma22:
                    return MathF.Pow(NonNegative(value), 2.2f);
                case TransferFunction.Gamma24:
                    return MathF.Pow(NonNegative(value), 2.4f);
                case TransferFunction.Rec709:
                    return value < 0.081f
                        ? value / 4.5f
                        : MathF.Pow((value + 0.099f) / 1.099f, 1.0f / 0.45f);
                case TransferFunction.Pq:
                    float powered = MathF.Pow(NonNegative(value), 1.0f / 78.84375f);
                    return MathF.Pow(
                        NonNegative(powered - 0.8359375f) /
                        (18.8515625f - (18.6875f * powered)),
                        1.0f / 0.1593017578125f);
                case TransferFunction.Hlg:
                    return value <= 0.5f
                        ? (value * value) / 3.0f
                        : (MathF.Exp((value - 0.55991073f) / 0.17883277f) + 0.28466892f) / 12.0f;
                default:
                    return value;
            }
        }

        private static ColorMatrix3x3 GetAdaptationMatrix(float[] fromWhite, float[] toWhite)
        {
            Vector3 fromCone = Transform(Bradford, fromWhite);
            Vector3 toCone = Transform(Bradford, toWhite);
            ColorMatrix3x3 diagonal = new ColorMatrix3x3(
                fromCone.X != 0.0f ? toCone.X / fromCone.X : 1.0f, 0.0f, 0.0f,
                0.0f, fromCone.Y != 0.0f ? toCone.Y / fromCone.Y : 1.0f, 0.0f,
                0.0f, 0.0f, fromCone.Z != 0.0f ? toCone.Z / fromCone.Z : 1.0f);
            return Multiply(BradfordInverse, Multiply(diagonal, Bradford));
        }

        private static Vector3 Transform(ColorMatrix3x3 matrix, float[] vector)
        {
            return new Vector3(
                (matrix.M11 * vector[0]) + (matrix.M12 * vector[1]) + (matrix.M13 * vector[2]),
                (matrix.M21 * vector[0]) + (matrix.M22 * vector[1]) + (matrix.M23 * vector[2]),
                (matrix.M31 * vector[0]) + (matrix.M32 * vector[1]) + (matrix.M33 * vector[2]));
        }

        private static ColorMatrix3x3 Multiply(ColorMatrix3x3 left, ColorMatrix3x3 right)
        {
            return new ColorMatrix3x3(
                (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31),
                (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32),
                (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
                (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31),
                (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32),
                (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
                (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31),
                (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32),
                (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));
        }

        private static float AcesNarkowicz(float value)
        {
            return Clamp01(
                (value * ((2.51f * value) + 0.03f)) /
                (value * ((2.43f * value) + 0.59f) + 0.14f));
        }

        private static float HableCurve(float value, ToneMapParameters parameters)
        {
            return
                ((value * ((parameters.A * value) + (parameters.C * parameters.B)) +
                  (parameters.D * parameters.E)) /
                 (value * ((parameters.A * value) + parameters.B) +
                  (parameters.D * parameters.F))) -
                (parameters.E / parameters.F);
        }

        private static float Clamp01(float value)
        {
            if (!(value > 0.0f))
            {
                return 0.0f;
            }

            return value > 1.0f ? 1.0f : value;
        }

        private static float NonNegative(float value)
        {
            return value > 0.0f ? value : 0.0f;
        }

        private static int ValidateInterleaved(
            ReadOnlySpan<float> source,
            Span<float> destination,
            int channels)
        {
            if (channels < 1 || channels > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            if (source.Length % channels != 0)
            {
                throw new ArgumentException("The source must contain complete interleaved pixels.", nameof(source));
            }

            if (destination.Length < source.Length)
            {
                throw new ArgumentException("The destination must be at least as long as the source.", nameof(destination));
            }

            return source.Length;
        }

        private static void ValidateAliasing(
            ReadOnlySpan<float> source,
            Span<float> destination,
            int elementCount)
        {
            if (source.Overlaps(destination.Slice(0, elementCount), out int elementOffset) && elementOffset != 0)
            {
                throw new ArgumentException(
                    "The source and destination may be identical, but must not partially overlap.",
                    nameof(destination));
            }
        }

        private static void ValidateDimensions(
            int sourceWidth,
            int sourceHeight,
            int destinationWidth,
            int destinationHeight,
            int channels)
        {
            if (sourceWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            }

            if (sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));
            }

            if (destinationWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationWidth));
            }

            if (destinationHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationHeight));
            }

            if (channels < 1 || channels > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }
        }
    }
}
