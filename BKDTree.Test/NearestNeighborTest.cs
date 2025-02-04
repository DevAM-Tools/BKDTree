using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BKDTree.Test;

public class NearestNeighborTest
{
    [TestCaseSource(typeof(NearestNeighborTest), nameof(TestCases))]
    public void GetNearestNeighbor(int count, int seed, bool parallel, bool bulkInsert)
    {
        Random random = new(seed);

        List<Point> points = Enumerable.Range(0, count).Select(value =>
        {
            double x = Point.GenerateValue(Pattern.Random, value, count, random);
            double y = Point.GenerateValue(Pattern.Random, value, count, random);
            Point point = new(x, y);
            return point;
        }).ToList();

        MetricBKDTree<Point> tree = new(2, parallel: parallel);

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

                Assert.That(tree.Count, Is.EqualTo(i + 1));
            }
        }

        foreach (Point point in points)
        {
            bool found = tree.Contains(point);

            Assert.That(found, Is.EqualTo(true));
        }

        double x = Point.GenerateValue(Pattern.Random, 0, count, random);
        double y = Point.GenerateValue(Pattern.Random, 0, count, random);
        Point targetPoint = new(x, y);

        Option<Point> expectedNearestNeighbor = default;
        double? minSquaredDistance = null;

        for (int i = 0; i < points.Count; i++)
        {
            Point point = points[i];
            double squaredDistance = MetricKDTree<Point>.GetSquaredDistance(ref point, ref targetPoint, 2);
            if (!minSquaredDistance.HasValue || squaredDistance < minSquaredDistance.Value)
            {
                expectedNearestNeighbor = point;
                minSquaredDistance = squaredDistance;
            }
        }
        bool neighborFound = tree.GetNearestNeighbor(targetPoint, out Point actualNearestNeighbor, out double actualMinSquaredDistance);

        Assert.That(neighborFound, Is.EqualTo(true));
        Assert.That(actualNearestNeighbor, Is.EqualTo(expectedNearestNeighbor.Value));
    }

    public static IEnumerable TestCases
    {
        get
        {
            int[] counts = [10, 500];
            int[] seeds = Enumerable.Range(0, 10000).ToArray();
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
                            yield return new TestCaseData(count, seed, parallel, bulkInsert);
                        }
                    }
                }
            }
        }
    }
}