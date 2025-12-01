using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class MetricKDTree<T, TMetric> : KDTree<T, MetricComparer<T, TMetric>>
    where T : notnull
    where TMetric : struct, IDimensionalMetric<T>
{
    internal readonly TMetric Metric;

    public MetricKDTree(int dimensionCount, IEnumerable<T> values, TMetric metric, bool parallel = false)
        : this(dimensionCount, values, metric, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public MetricKDTree(int dimensionCount, IEnumerable<T> values, TMetric metric, int maxThreadCount)
        : base(dimensionCount, values, new MetricComparer<T, TMetric>(metric), maxThreadCount)
    {
        Metric = metric;
    }

    internal MetricKDTree(int dimensionCount, IList<Segment<T>> values, TMetric metric, DimensionalComparer<T, MetricComparer<T, TMetric>>[] comparers, int maxThreadCount)
        : base(dimensionCount, values, new MetricComparer<T, TMetric>(metric), comparers, maxThreadCount)
    {
        Metric = metric;
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
        // Use sentinel value instead of nullable to avoid boxing overhead
        double minSquaredDistance = double.MaxValue;

        GetNearestNeighbor(ref value, ref currentNeighbor, ref minSquaredDistance, 0, Count - 1, 0);

        // Check HasValue before accessing Value to avoid returning default(T) for empty trees
        if (currentNeighbor.HasValue)
        {
            neighbor = currentNeighbor.Value;
            squaredDistance = minSquaredDistance;
            return true;
        }

        neighbor = default;
        squaredDistance = 0.0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetNearestNeighbor(ref T value, ref Option<T> neighbor, ref double minSquaredDistance, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        double squaredDistance = GetSquaredDistance(ref value, ref midValue);

        if (squaredDistance < minSquaredDistance)
        {
            neighbor = midValue;
            minSquaredDistance = squaredDistance;
        }

        int nextDimension = dimension + 1;
        if (nextDimension >= DimensionCount) nextDimension = 0;
        
        // Cache dimension values to avoid repeated delegate calls
        double midDimValue = Metric.GetDimension(in midValue, dimension);
        double valueDimValue = Metric.GetDimension(in value, dimension);
        int comparisonResult = valueDimValue.CompareTo(midDimValue);
        
        double axisDiff = midDimValue - valueDimValue;
        double axisSquaredDistance = axisDiff * axisDiff;

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

            if (axisSquaredDistance < minSquaredDistance)
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

            if (!wasRight && axisSquaredDistance < minSquaredDistance)
            {
                int nextLeftIdx = midIndex + 1;
                int nextRightIdx = rightIndex;

                if (nextRightIdx >= nextLeftIdx)
                {
                    GetNearestNeighbor(ref value, ref neighbor, ref minSquaredDistance, nextLeftIdx, nextRightIdx, nextDimension);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double GetSquaredDistance(ref T source, ref T target)
    {
        // Unrolled paths for common dimension counts to avoid loop overhead
        // JIT can better optimize these fixed-size calculations
        switch (DimensionCount)
        {
            case 2:
            {
                double d0 = Metric.GetDimension(in target, 0) - Metric.GetDimension(in source, 0);
                double d1 = Metric.GetDimension(in target, 1) - Metric.GetDimension(in source, 1);
                return d0 * d0 + d1 * d1;
            }
            case 3:
            {
                double d0 = Metric.GetDimension(in target, 0) - Metric.GetDimension(in source, 0);
                double d1 = Metric.GetDimension(in target, 1) - Metric.GetDimension(in source, 1);
                double d2 = Metric.GetDimension(in target, 2) - Metric.GetDimension(in source, 2);
                return d0 * d0 + d1 * d1 + d2 * d2;
            }
            case 4:
            {
                double d0 = Metric.GetDimension(in target, 0) - Metric.GetDimension(in source, 0);
                double d1 = Metric.GetDimension(in target, 1) - Metric.GetDimension(in source, 1);
                double d2 = Metric.GetDimension(in target, 2) - Metric.GetDimension(in source, 2);
                double d3 = Metric.GetDimension(in target, 3) - Metric.GetDimension(in source, 3);
                return d0 * d0 + d1 * d1 + d2 * d2 + d3 * d3;
            }
            default:
            {
                double result = 0;
                for (int dimension = 0; dimension < DimensionCount; dimension++)
                {
                    double diff = Metric.GetDimension(in target, dimension) - Metric.GetDimension(in source, dimension);
                    result += diff * diff;
                }
                return result;
            }
        }
    }
}