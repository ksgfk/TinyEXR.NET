using System;
using System.Numerics;

namespace TinyEXR.V3
{
    /// <summary>
    /// Incremental separable image resizer with memory bounded by filter support.
    /// Call <c>PullRow</c> until it returns <see cref="ExrResult.WouldBlock"/>,
    /// then push the next source row in increasing order.
    /// </summary>
    public sealed class StreamingImageResizer : IDisposable
    {
        private const int MaximumDimension = 1 << 20;

        private readonly AxisFilter _horizontal;
        private readonly AxisFilter _vertical;
        private readonly float[][] _ring;
        private readonly int[] _ringSourceRows;
        private readonly float[] _sourceScratch;
        private readonly float[] _accumulator;
        private int _nextSourceY;
        private int _currentDestinationY;
        private bool _disposed;

        public StreamingImageResizer(
            int sourceWidth,
            int sourceHeight,
            int destinationWidth,
            int destinationHeight,
            int channels,
            PixelType ioType = PixelType.Float,
            ResizeFilter filter = ResizeFilter.Mitchell,
            EdgeMode edgeMode = EdgeMode.Clamp)
        {
            ValidateDimension(sourceWidth, nameof(sourceWidth));
            ValidateDimension(sourceHeight, nameof(sourceHeight));
            ValidateDimension(destinationWidth, nameof(destinationWidth));
            ValidateDimension(destinationHeight, nameof(destinationHeight));
            if (channels < 1 || channels > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            ModelValidation.ValidateEnum(ioType, nameof(ioType));
            ModelValidation.ValidateEnum(filter, nameof(filter));
            ModelValidation.ValidateEnum(edgeMode, nameof(edgeMode));

            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            DestinationWidth = destinationWidth;
            DestinationHeight = destinationHeight;
            Channels = channels;
            IOType = ioType;
            Filter = filter;
            EdgeMode = edgeMode;

            _horizontal = AxisFilter.Build(sourceWidth, destinationWidth, filter, edgeMode);
            _vertical = AxisFilter.Build(sourceHeight, destinationHeight, filter, edgeMode);
            int ringCapacity = Math.Max(_vertical.MaximumContributorCount, 1);
            int destinationRowLength = checked(destinationWidth * channels);
            _ring = new float[ringCapacity][];
            _ringSourceRows = new int[ringCapacity];
            Array.Fill(_ringSourceRows, -1);
            for (int index = 0; index < ringCapacity; index++)
            {
                _ring[index] = new float[destinationRowLength];
            }

            _sourceScratch = new float[checked(sourceWidth * channels)];
            _accumulator = new float[destinationRowLength];
        }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int DestinationWidth { get; }

        public int DestinationHeight { get; }

        public int Channels { get; }

        public PixelType IOType { get; }

        public ResizeFilter Filter { get; }

        public EdgeMode EdgeMode { get; }

        public int NextSourceY => _nextSourceY;

        public int NextDestinationY => _currentDestinationY;

        public bool IsComplete => _currentDestinationY >= DestinationHeight;

        public void PushRow(int sourceY, ReadOnlySpan<float> sourceRow)
        {
            ValidatePush(sourceY, sourceRow.Length, PixelType.Float);
            sourceRow.Slice(0, _sourceScratch.Length).CopyTo(_sourceScratch);
            HorizontalResample(sourceY);
        }

        public void PushRow(int sourceY, ReadOnlySpan<ushort> sourceRow)
        {
            ValidatePush(sourceY, sourceRow.Length, PixelType.Half);
            PixelConversion.HalfToFloat(
                sourceRow.Slice(0, _sourceScratch.Length),
                _sourceScratch);
            HorizontalResample(sourceY);
        }

        public void PushRow(int sourceY, ReadOnlySpan<uint> sourceRow)
        {
            ValidatePush(sourceY, sourceRow.Length, PixelType.UInt);
            for (int index = 0; index < _sourceScratch.Length; index++)
            {
                _sourceScratch[index] = sourceRow[index];
            }

            HorizontalResample(sourceY);
        }

        public ExrResult PullRow(Span<float> destinationRow, out int destinationY)
        {
            ValidatePullType(PixelType.Float);
            ExrResult result = PrepareDestinationRow(destinationRow.Length, out destinationY);
            if (result != ExrResult.Success || destinationY >= DestinationHeight)
            {
                return result;
            }

            _accumulator.AsSpan().CopyTo(destinationRow);
            return ExrResult.Success;
        }

        public ExrResult PullRow(Span<ushort> destinationRow, out int destinationY)
        {
            ValidatePullType(PixelType.Half);
            ExrResult result = PrepareDestinationRow(destinationRow.Length, out destinationY);
            if (result != ExrResult.Success || destinationY >= DestinationHeight)
            {
                return result;
            }

            PixelConversion.FloatToHalf(_accumulator, destinationRow);
            return ExrResult.Success;
        }

        public ExrResult PullRow(Span<uint> destinationRow, out int destinationY)
        {
            ValidatePullType(PixelType.UInt);
            ExrResult result = PrepareDestinationRow(destinationRow.Length, out destinationY);
            if (result != ExrResult.Success || destinationY >= DestinationHeight)
            {
                return result;
            }

            for (int index = 0; index < _accumulator.Length; index++)
            {
                destinationRow[index] = NarrowToUInt32(_accumulator[index]);
            }

            return ExrResult.Success;
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private void ValidatePush(int sourceY, int rowLength, PixelType suppliedType)
        {
            ThrowIfDisposed();
            if (IOType != suppliedType)
            {
                throw new InvalidOperationException(
                    $"This resizer accepts {IOType} rows, not {suppliedType} rows.");
            }

            if (sourceY != _nextSourceY || sourceY >= SourceHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceY),
                    sourceY,
                    $"The next source row must be {_nextSourceY}.");
            }

            if (rowLength < _sourceScratch.Length)
            {
                throw new ArgumentException(
                    $"A source row must contain at least {_sourceScratch.Length} samples.",
                    "sourceRow");
            }
        }

        private void ValidatePullType(PixelType suppliedType)
        {
            ThrowIfDisposed();
            if (IOType != suppliedType)
            {
                throw new InvalidOperationException(
                    $"This resizer emits {IOType} rows, not {suppliedType} rows.");
            }
        }

        private ExrResult PrepareDestinationRow(int destinationLength, out int destinationY)
        {
            if (_currentDestinationY >= DestinationHeight)
            {
                destinationY = DestinationHeight;
                return ExrResult.Success;
            }

            AxisContributors contributors = _vertical.GetContributors(_currentDestinationY);
            for (int contributor = 0; contributor < contributors.Count; contributor++)
            {
                int sourceY = contributors.First + contributor;
                if (_ringSourceRows[sourceY % _ring.Length] != sourceY)
                {
                    destinationY = -1;
                    return ExrResult.WouldBlock;
                }
            }

            if (destinationLength < _accumulator.Length)
            {
                throw new ArgumentException(
                    $"A destination row must contain at least {_accumulator.Length} samples.",
                    "destinationRow");
            }

            Array.Clear(_accumulator, 0, _accumulator.Length);
            for (int contributor = 0; contributor < contributors.Count; contributor++)
            {
                float[] source = _ring[(contributors.First + contributor) % _ring.Length];
                Accumulate(_accumulator, source, contributors.Weights[contributor]);
            }

            destinationY = _currentDestinationY;
            _currentDestinationY++;
            return ExrResult.Success;
        }

        private void HorizontalResample(int sourceY)
        {
            int slot = sourceY % _ring.Length;
            float[] destination = _ring[slot];
            for (int destinationX = 0; destinationX < DestinationWidth; destinationX++)
            {
                AxisContributors contributors = _horizontal.GetContributors(destinationX);
                int destinationOffset = destinationX * Channels;
                for (int channel = 0; channel < Channels; channel++)
                {
                    float sum = 0.0f;
                    for (int contributor = 0; contributor < contributors.Count; contributor++)
                    {
                        int sourceOffset = ((contributors.First + contributor) * Channels) + channel;
                        sum += contributors.Weights[contributor] * _sourceScratch[sourceOffset];
                    }

                    destination[destinationOffset + channel] = sum;
                }
            }

            _ringSourceRows[slot] = sourceY;
            _nextSourceY++;
        }

        private static void Accumulate(float[] accumulator, float[] source, float weight)
        {
            int index = 0;
            if (Vector.IsHardwareAccelerated && accumulator.Length >= Vector<float>.Count)
            {
                Vector<float> vectorWeight = new Vector<float>(weight);
                int vectorEnd = accumulator.Length - (accumulator.Length % Vector<float>.Count);
                for (; index < vectorEnd; index += Vector<float>.Count)
                {
                    Vector<float> accumulated = new Vector<float>(accumulator, index);
                    Vector<float> values = new Vector<float>(source, index);
                    (accumulated + (values * vectorWeight)).CopyTo(accumulator, index);
                }
            }

            for (; index < accumulator.Length; index++)
            {
                accumulator[index] += source[index] * weight;
            }
        }

        private static uint NarrowToUInt32(float value)
        {
            double widened = value;
            if (!(widened > 0.0))
            {
                return 0U;
            }

            if (widened >= uint.MaxValue)
            {
                return uint.MaxValue;
            }

            return (uint)Math.Round(widened, MidpointRounding.ToEven);
        }

        private static void ValidateDimension(int value, string parameterName)
        {
            if (value <= 0 || value > MaximumDimension)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    $"Dimensions must be between 1 and {MaximumDimension}.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(StreamingImageResizer));
            }
        }

        private readonly struct AxisContributors
        {
            public AxisContributors(int first, int count, float[] weights)
            {
                First = first;
                Count = count;
                Weights = weights;
            }

            public int First { get; }

            public int Count { get; }

            public float[] Weights { get; }
        }

        private sealed class AxisFilter
        {
            private readonly int[] _first;
            private readonly int[] _count;
            private readonly float[][] _weights;

            private AxisFilter(int[] first, int[] count, float[][] weights, int maximumContributorCount)
            {
                _first = first;
                _count = count;
                _weights = weights;
                MaximumContributorCount = maximumContributorCount;
            }

            public int MaximumContributorCount { get; }

            public AxisContributors GetContributors(int destinationIndex)
            {
                return new AxisContributors(
                    _first[destinationIndex],
                    _count[destinationIndex],
                    _weights[destinationIndex]);
            }

            public static AxisFilter Build(
                int sourceSize,
                int destinationSize,
                ResizeFilter filter,
                EdgeMode edgeMode)
            {
                float filterScale = destinationSize < sourceSize
                    ? (float)sourceSize / destinationSize
                    : 1.0f;
                float support = GetFilterRadius(filter) * filterScale;
                float inverseScale = 1.0f / filterScale;
                int[] first = new int[destinationSize];
                int[] count = new int[destinationSize];
                float[][] weights = new float[destinationSize][];
                int maximumContributorCount = 0;

                for (int destinationIndex = 0; destinationIndex < destinationSize; destinationIndex++)
                {
                    float center =
                        ((destinationIndex + 0.5f) * ((float)sourceSize / destinationSize)) - 0.5f;
                    int lower = CeilingToInt(center - support);
                    int upper = FloorToInt(center + support);
                    if (upper < lower)
                    {
                        upper = lower;
                    }

                    int lowestMapped = sourceSize;
                    int highestMapped = -1;
                    for (int sourceIndex = lower; sourceIndex <= upper; sourceIndex++)
                    {
                        int mapped = MapEdge(sourceIndex, sourceSize, edgeMode);
                        lowestMapped = Math.Min(lowestMapped, mapped);
                        highestMapped = Math.Max(highestMapped, mapped);
                    }

                    if (highestMapped < lowestMapped)
                    {
                        lowestMapped = 0;
                        highestMapped = 0;
                    }

                    int contributorCount = checked(highestMapped - lowestMapped + 1);
                    first[destinationIndex] = lowestMapped;
                    count[destinationIndex] = contributorCount;
                    maximumContributorCount = Math.Max(maximumContributorCount, contributorCount);
                    float[] contributorWeights = new float[contributorCount];
                    float sum = 0.0f;
                    for (int sourceIndex = lower; sourceIndex <= upper; sourceIndex++)
                    {
                        int mapped = MapEdge(sourceIndex, sourceSize, edgeMode);
                        float weight = GetFilterWeight(
                            filter,
                            (center - sourceIndex) * inverseScale);
                        contributorWeights[mapped - lowestMapped] += weight;
                    }

                    for (int index = 0; index < contributorWeights.Length; index++)
                    {
                        sum += contributorWeights[index];
                    }

                    if (sum == 0.0f)
                    {
                        contributorWeights[0] = 1.0f;
                    }
                    else
                    {
                        float inverseSum = 1.0f / sum;
                        for (int index = 0; index < contributorWeights.Length; index++)
                        {
                            contributorWeights[index] *= inverseSum;
                        }
                    }

                    weights[destinationIndex] = contributorWeights;
                }

                return new AxisFilter(first, count, weights, maximumContributorCount);
            }

            private static float GetFilterWeight(ResizeFilter filter, float distance)
            {
                float absolute = Math.Abs(distance);
                switch (filter)
                {
                    case ResizeFilter.Box:
                        return absolute <= 0.5f ? 1.0f : 0.0f;
                    case ResizeFilter.Triangle:
                        return absolute < 1.0f ? 1.0f - absolute : 0.0f;
                    case ResizeFilter.CatmullRom:
                        return Cubic(absolute, 0.0f, 0.5f);
                    default:
                        return Cubic(absolute, 1.0f / 3.0f, 1.0f / 3.0f);
                }
            }

            private static float Cubic(float absolute, float b, float c)
            {
                if (absolute < 1.0f)
                {
                    return
                        (((12.0f - (9.0f * b) - (6.0f * c)) * absolute * absolute * absolute) +
                         ((-18.0f + (12.0f * b) + (6.0f * c)) * absolute * absolute) +
                         (6.0f - (2.0f * b))) /
                        6.0f;
                }

                if (absolute < 2.0f)
                {
                    return
                        (((-b - (6.0f * c)) * absolute * absolute * absolute) +
                         (((6.0f * b) + (30.0f * c)) * absolute * absolute) +
                         ((-12.0f * b - (48.0f * c)) * absolute) +
                         ((8.0f * b) + (24.0f * c))) /
                        6.0f;
                }

                return 0.0f;
            }

            private static float GetFilterRadius(ResizeFilter filter)
            {
                switch (filter)
                {
                    case ResizeFilter.Box:
                        return 0.5f;
                    case ResizeFilter.Triangle:
                        return 1.0f;
                    default:
                        return 2.0f;
                }
            }

            private static int MapEdge(int index, int size, EdgeMode edgeMode)
            {
                if (size == 1)
                {
                    return 0;
                }

                if (index >= 0 && index < size)
                {
                    return index;
                }

                if (edgeMode == EdgeMode.Clamp)
                {
                    return index < 0 ? 0 : size - 1;
                }

                if (edgeMode == EdgeMode.Wrap)
                {
                    index %= size;
                    return index < 0 ? index + size : index;
                }

                int period = (2 * size) - 2;
                index %= period;
                if (index < 0)
                {
                    index += period;
                }

                return index < size ? index : period - index;
            }

            private static int FloorToInt(float value)
            {
                int result = (int)value;
                return value < 0.0f && result != value ? result - 1 : result;
            }

            private static int CeilingToInt(float value)
            {
                int result = (int)value;
                return value > 0.0f && result != value ? result + 1 : result;
            }
        }
    }
}
