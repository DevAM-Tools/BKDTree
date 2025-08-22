using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BKDTree;
[DebuggerDisplay("Count: {Count}")]
public class MetricKDTree<T> : KDTree<T>
{
    internal readonly Func<T, int, double> GetDimension;

    public MetricKDTree(int dimensionCount, IEnumerable<T> values, Func<T, T, int, int> compareDimensionTo, Func<T, int, double> getDimension, bool parallel = false)
        : this(dimensionCount, values, compareDimensionTo, getDimension, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public MetricKDTree(int dimensionCount, IEnumerable<T> values, Func<T, T, int, int> compareDimensionTo, Func<T, int, double> getDimension, int maxThreadCount)
        : base(dimensionCount, values, compareDimensionTo, maxThreadCount)
    {
        GetDimension = getDimension
            ?? throw new ArgumentNullException(nameof(getDimension));
    }

    internal MetricKDTree(int dimensionCount, IList<Segment<T>> values, Func<T, T, int, int> compareDimensionTo, Func<T, int, double> getDimension, DimensionalComparer<T>[] comparers, int maxThreadCount)
        : base(dimensionCount, values, compareDimensionTo, comparers, maxThreadCount)
    {
        GetDimension = getDimension
            ?? throw new ArgumentNullException(nameof(getDimension));
    }

    /// <summary>
    /// Gets the value with the lowest Euclidean distance between it and the given <paramref name="value"/>.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="neighbor"></param>
    /// <param name="squaredDistance"></param>
    /// <returns>true if a neighbor was found otherwise false</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public bool GetNearestNeighbor(T value, out T neighbor, out double squaredDistance)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Option<T> currentNeighbor = default;
        double? minSquaredDistance = null;

        GetNearestNeighbor(ref value, ref currentNeighbor, ref minSquaredDistance, 0, Count - 1, 0);

        neighbor = currentNeighbor.Value;
        squaredDistance = minSquaredDistance ?? 0.0;
        bool result = currentNeighbor.HasValue;

        return result;
    }

    internal void GetNearestNeighbor(ref T value, ref Option<T> neighbor, ref double? minSquaredDistance, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        double squaredDistance = GetSquaredDistance(ref value, ref midValue, DimensionCount, GetDimension);

        if (!minSquaredDistance.HasValue || squaredDistance < minSquaredDistance.Value)
        {
            neighbor = midValue;
            minSquaredDistance = squaredDistance;
        }

        int nextDimension = (dimension + 1) % DimensionCount;
        int comparisonResult = CompareDimensionTo(value, midValue, dimension);

        bool wasRight = false;
        bool forceLeft = false;

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                GetNearestNeighbor(ref value, ref neighbor, ref minSquaredDistance, nextLeftIndex, nextRightIndex, nextDimension);

                wasRight = true;
            }

            double limitSquaredDistance = GetDimension(midValue, dimension) - GetDimension(value, dimension);
            limitSquaredDistance *= limitSquaredDistance;

            if (!minSquaredDistance.HasValue || limitSquaredDistance < minSquaredDistance.Value)
            {
                forceLeft = true;
            }
        }

        if (comparisonResult < 0 || (dirty && comparisonResult == 0) || forceLeft)
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                GetNearestNeighbor(ref value, ref neighbor, ref minSquaredDistance, nextLeftIndex, nextRightIndex, nextDimension);
            }

            if (!wasRight)
            {
                double squaredDistanceToLimit = GetDimension(midValue, dimension) - GetDimension(value, dimension);
                squaredDistanceToLimit *= squaredDistanceToLimit;

                if (!minSquaredDistance.HasValue || squaredDistanceToLimit < minSquaredDistance.Value)
                {
                    nextLeftIndex = midIndex + 1;
                    nextRightIndex = rightIndex;

                    if (nextRightIndex >= nextLeftIndex)
                    {
                        GetNearestNeighbor(ref value, ref neighbor, ref minSquaredDistance, nextLeftIndex, nextRightIndex, nextDimension);
                    }
                }
            }
        }
    }

    public static double GetSquaredDistance(ref T source, ref T target, int dimensionCount, Func<T, int, double> getDimension)
    {
        double result = 0;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            double diff = getDimension(target, dimension) - getDimension(source, dimension);

            result += diff * diff;
        }

        return result;
    }
}