using NUnit.Framework.Legacy;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BKDTree.Test;

public class CreateTest
{
    [TestCaseSource(typeof(CreateTest), nameof(TestCases))]
    public void Create(int blockSize, int count, Pattern xPattern, Pattern yPattern, int seed, bool parallel, bool bulkInsert)
    {
        Random random = new(seed);

        List<Point> points = Enumerable.Range(0, count).Select(value =>
        {
            double x = Point.GenerateValue(xPattern, value, count, random);
            double y = Point.GenerateValue(yPattern, value, count, random);
            Point point = new(x, y);
            return point;
        }).ToList();

        IEnumerable<Point> testPoints = bulkInsert ? points.Concat(points) : points;
        Dictionary<Point, Point[]> groupedPoints = testPoints
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.ToArray());

        BKDTree<Point> tree = new(2, blockSize, parallel);
        for (int i = 0; i < count; i++)
        {
            Point point = points[i];
            tree.Insert(point);

            Assert.That(tree.Count, Is.EqualTo(i + 1));
        }

        Assert.That(tree.Count, Is.EqualTo(points.Count));

        if (bulkInsert)
        {
            tree.Insert(points);

            Assert.That(tree.Count, Is.EqualTo(2 * points.Count));
        }

        foreach (Point point in points)
        {
            bool found = tree.Contains(point);

            Assert.That(found, Is.EqualTo(true));

            Point[] expectedPoints = groupedPoints[point].ToArray();

            List<Point> existingPointsList = [];
            tree.DoForEach(point, point =>
            {
                existingPointsList.Add(point);
                return false;
            });
            CollectionAssert.AreEquivalent(expectedPoints, existingPointsList);

            Point[] existingPoints = tree.Get(point).ToArray();

            CollectionAssert.AreEquivalent(expectedPoints, existingPoints);
        }
    }

    public static IEnumerable TestCases
    {
        get
        {
            int[] blockSizes = [2, 3, 4];
            int[] counts = [10, 50, 100, 500];
            Pattern[] patterns = [
                Pattern.Increasing,
                Pattern.LowerHalfIncreasing,
                Pattern.UpperHalfIncreasing,
                Pattern.Decreasing,
                Pattern.LowerHalfDecreasing,
                Pattern.UpperHalfDecreasing,
                Pattern.Random,
                Pattern.Const,
                Pattern.Alternating,
                Pattern.ReverseAlternating,
            ];
            int[] seeds = [0, 1, 2];
            bool[] parallels = [false, true];
            bool[] bulkInserts = [false, true];

            foreach (int blockSize in blockSizes)
            {
                foreach (int count in counts)
                {
                    foreach (Pattern xPattern in patterns)
                    {
                        foreach (Pattern yPattern in patterns)
                        {
                            for (int i = 0; i < seeds.Length; i++)
                            {
                                if (xPattern != Pattern.Random && yPattern != Pattern.Random && i > 0)
                                {
                                    continue;
                                }

                                foreach (bool parallel in parallels)
                                {
                                    foreach (bool bulkInsert in bulkInserts)
                                    {
                                        yield return new TestCaseData(blockSize, count, xPattern, yPattern, seeds[i], parallel, bulkInsert);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
