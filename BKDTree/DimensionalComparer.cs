using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BKDTree;

/// <summary>
/// Wraps an <see cref="IDimensionalComparer{T}"/> for use with <see cref="Array.Sort{TKey, TValue}(TKey[], TValue[], int, int, IComparer{TKey})"/>.
///
//// Note: This type is deliberately implemented as a class (reference type) to avoid boxing
//// when passed to APIs that take an IComparer<T> (for example Array.Sort). A struct implementing
//// IComparer<T> would be boxed when used via the interface, losing the benefit of being a value
//// type. By making this a class we avoid that boxing in those call sites.
////
//// In contrast, MetricComparer is a struct because it is intended to be used via generic
//// constraints (where TComparer : struct, IDimensionalComparer<T>) where the comparer is
//// passed as a generic parameter and no boxing occurs. This provides the allocation-free
//// behaviour for hot paths that use generic constraints.
/// </summary>
public class DimensionalComparer<T, TComparer>(int dimension, TComparer comparer) : IComparer<T>
    where TComparer : struct, IDimensionalComparer<T>
{
    public readonly int Dimension = dimension;
    private readonly TComparer _Comparer = comparer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(T left, T right)
    {
        int result = _Comparer.Compare(in left, in right, Dimension);
        return result;
    }
}

/// <summary>
/// A comparer that derives comparisons from metric dimension values.
/// This allows using only <see cref="IDimensionalMetric{T}"/> without a separate <see cref="IDimensionalComparer{T}"/>.
/// </summary>
/// <typeparam name="T">The type of values to compare</typeparam>
/// <typeparam name="TMetric">The metric type that provides dimension values</typeparam>
public readonly struct MetricComparer<T, TMetric> : IDimensionalComparer<T>
    where TMetric : struct, IDimensionalMetric<T>
{
    private readonly TMetric _metric;

    public MetricComparer(TMetric metric)
    {
        _metric = metric;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(in T left, in T right, int dimension)
    {
        double leftValue = _metric.GetDimension(in left, dimension);
        double rightValue = _metric.GetDimension(in right, dimension);
        return leftValue.CompareTo(rightValue);
    }
}
