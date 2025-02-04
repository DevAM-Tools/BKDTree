using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BKDTree;
[DebuggerDisplay("Count: {Count}")]
public class MetricKDTree<T> : KDTree<T> where T : IMetricTreeItem<T>
{
    public MetricKDTree(int dimensionCount, IEnumerable<T> values, bool parallel = false)
        : base(dimensionCount, values, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public MetricKDTree(int dimensionCount, IEnumerable<T> values, int maxThreadCount)
        : base(dimensionCount, values, maxThreadCount)
    {
    }

    internal MetricKDTree(int dimensionCount, IList<Segment<T>> values, DimensionalComparer<T>[] comparers, int maxThreadCount)
        : base(dimensionCount, values, comparers, maxThreadCount)
    {
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

        double squaredDistance = GetSquaredDistance(ref value, ref midValue, DimensionCount);

        if (!minSquaredDistance.HasValue || squaredDistance < minSquaredDistance.Value)
        {
            neighbor = midValue;
            minSquaredDistance = squaredDistance;
        }

        int nextDimension = (dimension + 1) % DimensionCount;
        int comparisonResult = value.CompareDimensionTo(midValue, dimension);

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

            double limitSquaredDistance = midValue.GetDimension(dimension) - value.GetDimension(dimension);
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
                double squaredDistanceToLimit = midValue.GetDimension(dimension) - value.GetDimension(dimension);
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

    public static double GetSquaredDistance(ref T source, ref T target, int dimensionCount)
    {
        double result = 0;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            double diff = target.GetDimension(dimension) - source.GetDimension(dimension);

            result += diff * diff;
        }

        return result;
    }
}