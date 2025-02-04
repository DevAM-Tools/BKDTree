using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class MetricBKDTree<T> : BKDTree<T> where T : IMetricTreeItem<T>
{
    public MetricBKDTree(int dimensionCount, int blockSize = DefaultBlockSize, bool parallel = false)
        : this(dimensionCount, blockSize, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public MetricBKDTree(int dimensionCount, int blockSize, int maxThreadCount)
        : base(dimensionCount, blockSize, maxThreadCount)
    {
    }

    internal override KDTree<T> CreateNewTree(IList<Segment<T>> values)
    {
        KDTree<T> result = new MetricKDTree<T>(DimensionCount, values, Comparers, MaxThreadCount);
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
        double? minSquaredDistance = null;

        for (int i = 0; i < BaseBlockCount; i++)
        {
            ref T currentValue = ref BaseBlock[i];

            double distance = MetricKDTree<T>.GetSquaredDistance(ref value, ref currentValue, DimensionCount);

            if (!minSquaredDistance.HasValue || distance < minSquaredDistance.Value)
            {
                currentNeighbor = currentValue;
                minSquaredDistance = distance;
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            MetricKDTree<T> tree = Trees[i] as MetricKDTree<T>;

            tree?.GetNearestNeighbor(ref value, ref currentNeighbor, ref minSquaredDistance, 0, tree.Count - 1, 0);
        }

        neighbor = currentNeighbor.Value;
        squaredDistance = minSquaredDistance ?? 0.0;
        bool result = currentNeighbor.HasValue;

        return result;
    }
}