using System;
using System.Runtime.CompilerServices;

namespace BKDTree.Test;


public readonly record struct Point(double X, double Y)
{
    public static double GenerateValue(Pattern pattern, int value, int count, Random random)
    {
        int half = count / 2;
        double result = pattern switch
        {
            Pattern.Increasing => value,
            Pattern.LowerHalfIncreasing => value < half ? value : half,
            Pattern.UpperHalfIncreasing => value > half ? value : half,
            Pattern.Decreasing => -value,
            Pattern.UpperHalfDecreasing => value > half ? -value : -half,
            Pattern.LowerHalfDecreasing => value < half ? -value : -half,
            Pattern.Random => 10.0 * Math.Round(random.NextDouble(), 4),
            Pattern.Alternating => value % 2 == 0 ? value : -value,
            Pattern.ReverseAlternating => value % 2 == 0 ? count - value : -count + value,
            _ => 0
        };
        return result;
    }
}

/// <summary>
/// Struct-based comparer for Point that implements IDimensionalComparer.
/// Using a struct enables the JIT to inline comparison calls.
/// </summary>
public readonly struct PointComparer : IDimensionalComparer<Point>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Compare(in Point left, in Point right, int dimension)
    {
        return dimension switch
        {
            0 => left.X < right.X ? -1 : left.X > right.X ? 1 : 0,
            1 => left.Y < right.Y ? -1 : left.Y > right.Y ? 1 : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
    }
}

/// <summary>
/// Struct-based metric for Point that implements IDimensionalMetric.
/// Using a struct enables the JIT to inline dimension access calls.
/// </summary>
public readonly struct PointMetric : IDimensionalMetric<Point>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDimension(in Point value, int dimension)
    {
        return dimension switch
        {
            0 => value.X,
            1 => value.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
    }
}
