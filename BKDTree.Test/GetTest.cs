using NUnit.Framework.Legacy;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BKDTree.Test;

public class GetTest
{
    [TestCaseSource(typeof(GetTest), nameof(TestCases))]
    public void GetRange(int blockSize, int count, Pattern xPattern, Pattern yPattern, int seed, bool parallel, bool bulkInsert, double? lowerLimitShareX, double? lowerLimitShareY, double? upperLimitShareX, double? upperLimitShareY)
    {
        Random random = new(seed);

        List<Point> points = Enumerable.Range(0, count).Select(value =>
        {
            double x = Point.GenerateValue(xPattern, value, count, random);
            double y = Point.GenerateValue(yPattern, value, count, random);
            Point point = new(x, y);
            return point;
        }).ToList();

        BKDTree<Point> tree = new(2, blockSize, parallel);

        Point minPoint = points[0];
        Point maxPoint = points[0];

        foreach (Point point in points)
        {
            minPoint = point.X < minPoint.X ? minPoint with { X = point.X } : minPoint;
            minPoint = point.Y < minPoint.Y ? minPoint with { Y = point.Y } : minPoint;
            maxPoint = point.X > maxPoint.X ? maxPoint with { X = point.X } : maxPoint;
            maxPoint = point.Y > maxPoint.Y ? maxPoint with { Y = point.Y } : maxPoint;
        }

        if (bulkInsert)
        {
            tree.Insert(points);
        }
        else
        {
            foreach (Point point in points)
            {
                tree.Insert(point);
            }

        }

        Option<Point> lowerLimit = lowerLimitShareX.HasValue && lowerLimitShareY.HasValue ? new Option<Point>(true, new(minPoint.X + lowerLimitShareX.Value * (maxPoint.X - minPoint.X), minPoint.Y + lowerLimitShareY.Value * (maxPoint.Y - minPoint.Y))) : default;
        Option<Point> upperLimit = upperLimitShareX.HasValue && upperLimitShareY.HasValue ? new Option<Point>(true, new(minPoint.X + upperLimitShareX.Value * (maxPoint.X - minPoint.X), minPoint.Y + upperLimitShareY.Value * (maxPoint.Y - minPoint.Y))) : default;

        List<Point> expectedRange = points.Where(point => (!lowerLimit.HasValue || (point.X >= lowerLimit.Value.X && point.Y >= lowerLimit.Value.Y))
                                                          && (!upperLimit.HasValue || (point.X <= upperLimit.Value.X && point.Y <= upperLimit.Value.Y))).ToList();

        List<Point> actualRange = [];
        tree.DoForEach(point =>
        {
            actualRange.Add(point);
            return false;
        }, lowerLimit, upperLimit, true);

        expectedRange.Sort((left, right) => left.X < right.X ? -1 : left.X > right.X ? 1 : left.Y < right.Y ? -1 : left.Y > right.Y ? 1 : 0);
        actualRange.Sort((left, right) => left.X < right.X ? -1 : left.X > right.X ? 1 : left.Y < right.Y ? -1 : left.Y > right.Y ? 1 : 0);

        Point firstPoint = default;
        bool hasFirstPoint = tree.TryGetFirst(lowerLimit, upperLimit, true, ref firstPoint);
        if (expectedRange.Count > 0)
        {
            Assert.That(hasFirstPoint, Is.EqualTo(true));
            Assert.That(expectedRange.Contains(firstPoint), Is.EqualTo(true));
        }
        else
        {
            Assert.That(hasFirstPoint, Is.EqualTo(false));
        }

        CollectionAssert.AreEqual(expectedRange, actualRange);
    }

    public static IEnumerable TestCases
    {
        get
        {
            int[] blockSizes = [2, 3, 4];
            int[] counts = [10, 500];
            Pattern[] patterns = [
                Pattern.Random,
                Pattern.Const,
            ];
            int[] seeds = [0, 1];
            bool[] parallels = [false, true];
            bool[] bulkInserts = [false, true];
            double?[] limitShares = [null, -1.0, 0.0, 0.5, 1.0, 2.0];

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
                                        foreach (double? lowerLimitShareX in limitShares)
                                        {
                                            foreach (double? lowerLimitShareY in limitShares)
                                            {
                                                foreach (double? upperLimitShareX in limitShares)
                                                {
                                                    foreach (double? upperLimitShareY in limitShares)
                                                    {
                                                        yield return new TestCaseData(blockSize, count, xPattern, yPattern, seeds[i], parallel, bulkInsert, lowerLimitShareX, lowerLimitShareY, upperLimitShareX, upperLimitShareY);
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
            }

        }
    }
}
