using NUnit.Framework.Legacy;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BKDTree.Test.KDTree;

public class CreateTest
{
    [TestCaseSource(typeof(CreateTest), nameof(TestCases))]
    public void Create(int count, Pattern xPattern, Pattern yPattern, int seed, bool parallel)
    {
        Random random = new(seed);

        Point[] points = Enumerable.Range(0, count).Select(value =>
        {
            double x = Point.GenerateValue(xPattern, value, count, random);
            double y = Point.GenerateValue(yPattern, value, count, random);
            Point point = new(x, y);
            return point;
        }).ToArray();

        Dictionary<Point, Point[]> groupedPoints = points.GroupBy(x => x).ToDictionary(x => x.Key, x => x.ToArray());

        KDTree<Point> tree = new(2, points, parallel);

        Assert.That(tree.Count, Is.EqualTo(points.Length));

        foreach (Point point in points)
        {
            bool found = tree.Contains(point);

            Assert.That(found, Is.EqualTo(true));

            Point[] existingPoints = tree.Get(point).ToArray();
            Point[] expectedPoints = groupedPoints[point].ToArray();

            CollectionAssert.AreEquivalent(expectedPoints, existingPoints);

            List<Point> existingPointsList = [];
            tree.DoForEach(point, point =>
            {
                existingPointsList.Add(point);
                return false;
            });
            CollectionAssert.AreEquivalent(expectedPoints, existingPointsList);
        }
    }

    public static IEnumerable TestCases
    {
        get
        {
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
                                yield return new TestCaseData(count, xPattern, yPattern, seeds[i], parallel);
                            }

                        }
                    }
                }
            }
        }
    }
}
