using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BKDTree;

public class DimensionalComparer<T>(int dimension, Func<T, T, int, int> compareDimensionTo) : IComparer<T>
{
    public readonly int Dimension = dimension;
    private readonly Func<T, T, int, int> _CompareDimensionTo = compareDimensionTo ?? throw new ArgumentNullException(nameof(compareDimensionTo));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(T left, T right)
    {
        int result = _CompareDimensionTo(left, right, Dimension);
        return result;
    }
}
