using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class MetricBKDTree<T, TMetric> : BKDTree<T, MetricComparer<T, TMetric>>
    where T : notnull
    where TMetric : struct, IDimensionalMetric<T>
{
    internal readonly TMetric Metric;

    public MetricBKDTree(int dimensionCount, TMetric metric, int blockSize = DefaultBlockSize, bool parallel = false)
        : this(dimensionCount, metric, blockSize, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public MetricBKDTree(int dimensionCount, TMetric metric, int blockSize, int maxThreadCount)
        : base(dimensionCount, new MetricComparer<T, TMetric>(metric), blockSize, maxThreadCount)
    {
        Metric = metric;
    }

    internal override KDTree<T, MetricComparer<T, TMetric>> CreateNewTree(IList<Segment<T>> values)
    {
        KDTree<T, MetricComparer<T, TMetric>> result = new MetricKDTree<T, TMetric>(DimensionCount, values, Metric, Comparers, MaxThreadCount);
        return result;
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            ref T currentValue = ref BaseBlock[i];

            double distance = GetSquaredDistance(ref value, ref currentValue);

            if (distance < minSquaredDistance)
            {
                currentNeighbor = currentValue;
                minSquaredDistance = distance;
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            MetricKDTree<T, TMetric> tree = Trees[i] as MetricKDTree<T, TMetric>;

            tree?.GetNearestNeighbor(ref value, ref currentNeighbor, ref minSquaredDistance, 0, tree.Count - 1, 0);
        }

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