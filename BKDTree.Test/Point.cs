using System;
using System.Runtime.CompilerServices;

namespace BKDTree.Test;


public readonly record struct Point(double X, double Y) : IMetricTreeItem<Point>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareDimensionTo(Point other, int dimension)
    {
        int result = dimension switch
        {
            0 => X < other.X ? -1 : X > other.X ? 1 : 0,
            1 => Y < other.Y ? -1 : Y > other.Y ? 1 : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDimension(int dimension)
    {
        double result = dimension switch
        {
            0 => X,
            1 => Y,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
        return result;
    }

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
