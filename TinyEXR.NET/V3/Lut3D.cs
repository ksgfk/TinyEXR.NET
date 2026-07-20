using System;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace TinyEXR.V3
{
    /// <summary>
    /// Owned baked 3D lookup table using TinyEXR's R-fastest sample layout.
    /// </summary>
    public sealed class Lut3D
    {
        private const int MaximumCubeSize = 256;
        private readonly float[] _data;

        public Lut3D(
            int size,
            ReadOnlySpan<float> data,
            Vector3? domainMinimum = null,
            Vector3? domainMaximum = null)
        {
            if (size < 2 || size > MaximumCubeSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    size,
                    $"A 3D LUT size must be between 2 and {MaximumCubeSize}.");
            }

            int expectedLength = checked(checked(checked(size * size) * size) * 3);
            if (data.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"A size-{size} 3D LUT requires exactly {expectedLength} float samples.",
                    nameof(data));
            }

            Size = size;
            DomainMinimum = domainMinimum ?? Vector3.Zero;
            DomainMaximum = domainMaximum ?? Vector3.One;
            ValidateDomain(DomainMinimum, nameof(domainMinimum));
            ValidateDomain(DomainMaximum, nameof(domainMaximum));
            _data = data.ToArray();
        }

        public int Size { get; }

        public Vector3 DomainMinimum { get; }

        public Vector3 DomainMaximum { get; }

        public ReadOnlySpan<float> Data => _data;

        public static Lut3D ParseCube(string text)
        {
            ExrResult result = TryParseCube(text, out Lut3D? lut);
            switch (result)
            {
                case ExrResult.Success:
                    return lut!;
                case ExrResult.InvalidArgument:
                    throw new ArgumentNullException(nameof(text));
                case ExrResult.Unsupported:
                    throw new NotSupportedException("Only .cube 3D LUTs with sizes from 2 through 256 are supported.");
                default:
                    throw new FormatException("The .cube 3D LUT is malformed or contains the wrong number of samples.");
            }
        }

        public static ExrResult TryParseCube(string? text, out Lut3D? lut)
        {
            lut = null;
            if (text == null)
            {
                return ExrResult.InvalidArgument;
            }

            int size = 0;
            float[]? data = null;
            int dataIndex = 0;
            Vector3 domainMinimum = Vector3.Zero;
            Vector3 domainMaximum = Vector3.One;

            using (StringReader reader = new StringReader(text))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ReadOnlySpan<char> content = line.AsSpan().Trim();
                    if (content.Length == 0 || content[0] == '#')
                    {
                        continue;
                    }

                    if (StartsWithKeyword(content, "TITLE"))
                    {
                        continue;
                    }

                    if (StartsWithKeyword(content, "LUT_1D_SIZE"))
                    {
                        return ExrResult.Unsupported;
                    }

                    if (TryGetKeywordValue(content, "LUT_3D_SIZE", out ReadOnlySpan<char> sizeText))
                    {
                        if (data != null || !TryParseFloat(sizeText, out float parsedSize))
                        {
                            return ExrResult.Corrupt;
                        }

                        size = (int)parsedSize;
                        if (size != parsedSize || size < 2 || size > MaximumCubeSize)
                        {
                            return ExrResult.Unsupported;
                        }

                        try
                        {
                            data = new float[checked(checked(checked(size * size) * size) * 3)];
                        }
                        catch (OverflowException)
                        {
                            return ExrResult.Corrupt;
                        }
                        catch (OutOfMemoryException)
                        {
                            return ExrResult.OutOfMemory;
                        }

                        continue;
                    }

                    if (TryGetKeywordValue(content, "DOMAIN_MIN", out ReadOnlySpan<char> domainMinimumText))
                    {
                        if (!TryParseTriple(domainMinimumText, out domainMinimum))
                        {
                            return ExrResult.Corrupt;
                        }

                        continue;
                    }

                    if (TryGetKeywordValue(content, "DOMAIN_MAX", out ReadOnlySpan<char> domainMaximumText))
                    {
                        if (!TryParseTriple(domainMaximumText, out domainMaximum))
                        {
                            return ExrResult.Corrupt;
                        }

                        continue;
                    }

                    char first = content[0];
                    if (first == '+' || first == '-' || first == '.' || char.IsDigit(first))
                    {
                        if (data == null || !TryParseTriple(content, out Vector3 sample) || dataIndex > data.Length - 3)
                        {
                            return ExrResult.Corrupt;
                        }

                        data[dataIndex++] = sample.X;
                        data[dataIndex++] = sample.Y;
                        data[dataIndex++] = sample.Z;
                    }
                }
            }

            if (data == null || dataIndex != data.Length)
            {
                return ExrResult.Corrupt;
            }

            try
            {
                lut = new Lut3D(size, data, domainMinimum, domainMaximum);
                return ExrResult.Success;
            }
            catch (OutOfMemoryException)
            {
                lut = null;
                return ExrResult.OutOfMemory;
            }
        }

        public void Apply(
            ReadOnlySpan<float> source,
            Span<float> destination,
            int channels,
            LutInterpolation interpolation = LutInterpolation.Trilinear)
        {
            if (channels < 3 || channels > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            ModelValidation.ValidateEnum(interpolation, nameof(interpolation));
            if (source.Length % channels != 0)
            {
                throw new ArgumentException("The source must contain complete interleaved pixels.", nameof(source));
            }

            if (destination.Length < source.Length)
            {
                throw new ArgumentException("The destination must be at least as long as the source.", nameof(destination));
            }

            if (source.Overlaps(destination.Slice(0, source.Length), out int offset) && offset != 0)
            {
                throw new ArgumentException(
                    "The source and destination may be identical, but must not partially overlap.",
                    nameof(destination));
            }

            int pixelCount = source.Length / channels;
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int sourceOffset = pixel * channels;
                GetGridCoordinate(source[sourceOffset], DomainMinimum.X, DomainMaximum.X, out int red, out float redFraction);
                GetGridCoordinate(source[sourceOffset + 1], DomainMinimum.Y, DomainMaximum.Y, out int green, out float greenFraction);
                GetGridCoordinate(source[sourceOffset + 2], DomainMinimum.Z, DomainMaximum.Z, out int blue, out float blueFraction);

                if (interpolation == LutInterpolation.Tetrahedral)
                {
                    ApplyTetrahedral(
                        destination.Slice(sourceOffset, 3),
                        red,
                        green,
                        blue,
                        redFraction,
                        greenFraction,
                        blueFraction);
                }
                else
                {
                    ApplyTrilinear(
                        destination.Slice(sourceOffset, 3),
                        red,
                        green,
                        blue,
                        redFraction,
                        greenFraction,
                        blueFraction);
                }

                if (channels == 4)
                {
                    destination[sourceOffset + 3] = source[sourceOffset + 3];
                }
            }
        }

        private void ApplyTetrahedral(
            Span<float> destination,
            int red,
            int green,
            int blue,
            float redFraction,
            float greenFraction,
            float blueFraction)
        {
            int c000 = GetOffset(red, green, blue);
            int c111 = GetOffset(red + 1, green + 1, blue + 1);
            int p100 = GetOffset(red + 1, green, blue);
            int p010 = GetOffset(red, green + 1, blue);
            int p001 = GetOffset(red, green, blue + 1);
            int p110 = GetOffset(red + 1, green + 1, blue);
            int p101 = GetOffset(red + 1, green, blue + 1);
            int p011 = GetOffset(red, green + 1, blue + 1);

            for (int channel = 0; channel < 3; channel++)
            {
                float value;
                if (redFraction >= greenFraction && greenFraction >= blueFraction)
                {
                    value =
                        _data[c000 + channel] +
                        (redFraction * (_data[p100 + channel] - _data[c000 + channel])) +
                        (greenFraction * (_data[p110 + channel] - _data[p100 + channel])) +
                        (blueFraction * (_data[c111 + channel] - _data[p110 + channel]));
                }
                else if (redFraction >= blueFraction && blueFraction >= greenFraction)
                {
                    value =
                        _data[c000 + channel] +
                        (redFraction * (_data[p100 + channel] - _data[c000 + channel])) +
                        (blueFraction * (_data[p101 + channel] - _data[p100 + channel])) +
                        (greenFraction * (_data[c111 + channel] - _data[p101 + channel]));
                }
                else if (blueFraction >= redFraction && redFraction >= greenFraction)
                {
                    value =
                        _data[c000 + channel] +
                        (blueFraction * (_data[p001 + channel] - _data[c000 + channel])) +
                        (redFraction * (_data[p101 + channel] - _data[p001 + channel])) +
                        (greenFraction * (_data[c111 + channel] - _data[p101 + channel]));
                }
                else if (greenFraction >= redFraction && redFraction >= blueFraction)
                {
                    value =
                        _data[c000 + channel] +
                        (greenFraction * (_data[p010 + channel] - _data[c000 + channel])) +
                        (redFraction * (_data[p110 + channel] - _data[p010 + channel])) +
                        (blueFraction * (_data[c111 + channel] - _data[p110 + channel]));
                }
                else if (greenFraction >= blueFraction && blueFraction >= redFraction)
                {
                    value =
                        _data[c000 + channel] +
                        (greenFraction * (_data[p010 + channel] - _data[c000 + channel])) +
                        (blueFraction * (_data[p011 + channel] - _data[p010 + channel])) +
                        (redFraction * (_data[c111 + channel] - _data[p011 + channel]));
                }
                else
                {
                    value =
                        _data[c000 + channel] +
                        (blueFraction * (_data[p001 + channel] - _data[c000 + channel])) +
                        (greenFraction * (_data[p011 + channel] - _data[p001 + channel])) +
                        (redFraction * (_data[c111 + channel] - _data[p011 + channel]));
                }

                destination[channel] = value;
            }
        }

        private void ApplyTrilinear(
            Span<float> destination,
            int red,
            int green,
            int blue,
            float redFraction,
            float greenFraction,
            float blueFraction)
        {
            int c000 = GetOffset(red, green, blue);
            int c100 = GetOffset(red + 1, green, blue);
            int c010 = GetOffset(red, green + 1, blue);
            int c110 = GetOffset(red + 1, green + 1, blue);
            int c001 = GetOffset(red, green, blue + 1);
            int c101 = GetOffset(red + 1, green, blue + 1);
            int c011 = GetOffset(red, green + 1, blue + 1);
            int c111 = GetOffset(red + 1, green + 1, blue + 1);
            for (int channel = 0; channel < 3; channel++)
            {
                float lowerLower = Lerp(_data[c000 + channel], _data[c100 + channel], redFraction);
                float lowerUpper = Lerp(_data[c001 + channel], _data[c101 + channel], redFraction);
                float upperLower = Lerp(_data[c010 + channel], _data[c110 + channel], redFraction);
                float upperUpper = Lerp(_data[c011 + channel], _data[c111 + channel], redFraction);
                float lower = Lerp(lowerLower, upperLower, greenFraction);
                float upper = Lerp(lowerUpper, upperUpper, greenFraction);
                destination[channel] = Lerp(lower, upper, blueFraction);
            }
        }

        private void GetGridCoordinate(
            float value,
            float domainMinimum,
            float domainMaximum,
            out int coordinate,
            out float fraction)
        {
            float range = domainMaximum - domainMinimum;
            float normalized = range != 0.0f ? (value - domainMinimum) / range : 0.0f;
            float grid = normalized * (Size - 1);
            if (!(grid > 0.0f))
            {
                grid = 0.0f;
            }

            if (grid > Size - 1)
            {
                grid = Size - 1;
            }

            coordinate = (int)grid;
            if (coordinate >= Size - 1)
            {
                coordinate = Size - 2;
            }

            fraction = grid - coordinate;
        }

        private int GetOffset(int red, int green, int blue)
        {
            return checked(((((blue * Size) + green) * Size) + red) * 3);
        }

        private static bool StartsWithKeyword(ReadOnlySpan<char> content, string keyword)
        {
            return content.StartsWith(keyword.AsSpan(), StringComparison.Ordinal) &&
                (content.Length == keyword.Length || char.IsWhiteSpace(content[keyword.Length]));
        }

        private static bool TryGetKeywordValue(
            ReadOnlySpan<char> content,
            string keyword,
            out ReadOnlySpan<char> value)
        {
            if (!StartsWithKeyword(content, keyword))
            {
                value = default;
                return false;
            }

            value = content.Slice(keyword.Length).Trim();
            return true;
        }

        private static bool TryParseTriple(ReadOnlySpan<char> text, out Vector3 result)
        {
            result = default;
            int offset = 0;
            if (!TryReadToken(text, ref offset, out ReadOnlySpan<char> first) ||
                !TryReadToken(text, ref offset, out ReadOnlySpan<char> second) ||
                !TryReadToken(text, ref offset, out ReadOnlySpan<char> third))
            {
                return false;
            }

            if (TryReadToken(text, ref offset, out ReadOnlySpan<char> trailing) &&
                (trailing.Length == 0 || trailing[0] != '#'))
            {
                return false;
            }

            if (!TryParseFloat(first, out float x) ||
                !TryParseFloat(second, out float y) ||
                !TryParseFloat(third, out float z))
            {
                return false;
            }

            result = new Vector3(x, y, z);
            return true;
        }

        private static bool TryReadToken(
            ReadOnlySpan<char> text,
            ref int offset,
            out ReadOnlySpan<char> token)
        {
            while (offset < text.Length && char.IsWhiteSpace(text[offset]))
            {
                offset++;
            }

            if (offset >= text.Length)
            {
                token = default;
                return false;
            }

            int start = offset;
            while (offset < text.Length && !char.IsWhiteSpace(text[offset]))
            {
                offset++;
            }

            token = text.Slice(start, offset - start);
            return true;
        }

        private static bool TryParseFloat(ReadOnlySpan<char> text, out float value)
        {
            int commentIndex = text.IndexOf('#');
            if (commentIndex >= 0)
            {
                text = text.Slice(0, commentIndex);
            }

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void ValidateDomain(Vector3 value, string parameterName)
        {
            if (float.IsNaN(value.X) || float.IsInfinity(value.X) ||
                float.IsNaN(value.Y) || float.IsInfinity(value.Y) ||
                float.IsNaN(value.Z) || float.IsInfinity(value.Z))
            {
                throw new ArgumentOutOfRangeException(parameterName, "LUT domains must be finite.");
            }
        }

        private static float Lerp(float left, float right, float amount)
        {
            return left + ((right - left) * amount);
        }
    }
}
