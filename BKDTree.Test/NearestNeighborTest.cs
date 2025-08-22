using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BKDTree.Test;

public class NearestNeighborTest
{
    [Test]
    [MethodDataSource(nameof(TestCases))]
    public async Task GetNearestNeighbor(int count, int seed, bool parallel, bool bulkInsert)
    {
        Random random = new(seed);

        List<Point> points = Enumerable.Range(0, count).Select(value =>
        {
            double x = Point.GenerateValue(Pattern.Random, value, count, random);
            double y = Point.GenerateValue(Pattern.Random, value, count, random);
            Point point = new(x, y);
            return point;
        }).ToList();

        MetricBKDTree<Point> tree = new(2, Point.CompareDimensionTo, Point.GetDimension, parallel: parallel);

        if (bulkInsert)
        {
            tree.Insert(points);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                Point point = points[i];
                tree.Insert(point);

                await Assert.That(tree.Count).IsEqualTo(i + 1);
            }
        }

        foreach (Point point in points)
        {
            bool found = tree.Contains(point);

            await Assert.That(found).IsEqualTo(true);
        }

        double x = Point.GenerateValue(Pattern.Random, 0, count, random);
        double y = Point.GenerateValue(Pattern.Random, 0, count, random);
        Point targetPoint = new(x, y);

        Option<Point> expectedNearestNeighbor = default;
        double? minSquaredDistance = null;

        for (int i = 0; i < points.Count; i++)
        {
            Point point = points[i];
            double squaredDistance = MetricKDTree<Point>.GetSquaredDistance(ref point, ref targetPoint, 2, Point.GetDimension);
            if (!minSquaredDistance.HasValue || squaredDistance < minSquaredDistance.Value)
            {
                expectedNearestNeighbor = point;
                minSquaredDistance = squaredDistance;
            }
        }
        bool neighborFound = tree.GetNearestNeighbor(targetPoint, out Point actualNearestNeighbor, out double actualMinSquaredDistance);

        await Assert.That(neighborFound).IsEqualTo(true);
        await Assert.That(actualNearestNeighbor).IsEqualTo(expectedNearestNeighbor.Value);
    }

    public static IEnumerable<object[]> TestCases()
    {
        int[] counts = [10, 500];
        int[] seeds = Enumerable.Range(0, 100).ToArray();
        bool[] parallels = [false, true];
        bool[] bulkInserts = [false, true];

        foreach (int count in counts)
        {
            foreach (int seed in seeds)
            {
                foreach (bool parallel in parallels)
                {
                    foreach (bool bulkInsert in bulkInserts)
                    {
                        yield return new object[] { count, seed, parallel, bulkInsert };
                    }
                }
            }
        }
    }
}
