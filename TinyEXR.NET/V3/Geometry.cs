using System;

namespace TinyEXR.V3
{
    /// <summary>
    /// Inclusive integer bounds used by OpenEXR data and display windows.
    /// </summary>
    public readonly struct Box2i
    {
        public Box2i(int minX, int minY, int maxX, int maxY)
        {
            if (maxX < minX)
            {
                throw new ArgumentException("The maximum X coordinate must not be less than the minimum X coordinate.", nameof(maxX));
            }

            if (maxY < minY)
            {
                throw new ArgumentException("The maximum Y coordinate must not be less than the minimum Y coordinate.", nameof(maxY));
            }

            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public int MinX { get; }

        public int MinY { get; }

        public int MaxX { get; }

        public int MaxY { get; }

        public long Width => (long)MaxX - MinX + 1L;

        public long Height => (long)MaxY - MinY + 1L;

        public bool Contains(Box2i other)
        {
            return other.MinX >= MinX && other.MaxX <= MaxX &&
                other.MinY >= MinY && other.MaxY <= MaxY;
        }
    }

    /// <summary>
    /// CIE xy primaries and white point in OpenEXR order.
    /// </summary>
    public readonly struct Chromaticities
    {
        public Chromaticities(
            float redX,
            float redY,
            float greenX,
            float greenY,
            float blueX,
            float blueY,
            float whiteX,
            float whiteY)
        {
            ModelValidation.ThrowIfNotFinite(redX, nameof(redX));
            ModelValidation.ThrowIfNotFinite(redY, nameof(redY));
            ModelValidation.ThrowIfNotFinite(greenX, nameof(greenX));
            ModelValidation.ThrowIfNotFinite(greenY, nameof(greenY));
            ModelValidation.ThrowIfNotFinite(blueX, nameof(blueX));
            ModelValidation.ThrowIfNotFinite(blueY, nameof(blueY));
            ModelValidation.ThrowIfNotFinite(whiteX, nameof(whiteX));
            ModelValidation.ThrowIfNotFinite(whiteY, nameof(whiteY));

            RedX = redX;
            RedY = redY;
            GreenX = greenX;
            GreenY = greenY;
            BlueX = blueX;
            BlueY = blueY;
            WhiteX = whiteX;
            WhiteY = whiteY;
        }

        public float RedX { get; }

        public float RedY { get; }

        public float GreenX { get; }

        public float GreenY { get; }

        public float BlueX { get; }

        public float BlueY { get; }

        public float WhiteX { get; }

        public float WhiteY { get; }
    }
}
