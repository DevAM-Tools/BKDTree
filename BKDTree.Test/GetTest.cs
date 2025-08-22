using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;
using TUnit.Assertions;

namespace BKDTree.Test;

public class GetTest
{
    [Test]
    [MethodDataSource(nameof(TestCases))]
    public async Task GetRange(int blockSize, Pattern xPattern, Pattern yPattern, bool parallel, bool bulkInsert, double? lowerLimitShareX, double? lowerLimitShareY, double? upperLimitShareX, double? upperLimitShareY)
    {
        int[] counts = [10, 500];
        int[] seeds = xPattern == Pattern.Random || yPattern == Pattern.Random ? [0, 1, 2] : [0];

        // count and seed is moved to this place to reduce exponential growth of the number of test cases
        foreach (int count in counts)
        {
            foreach (int seed in seeds)
            {
                Random random = new(seed);

                List<Point> points = Enumerable.Range(0, count).Select(value =>
                {
                    double x = Point.GenerateValue(xPattern, value, count, random);
                    double y = Point.GenerateValue(yPattern, value, count, random);
                    Point point = new(x, y);
                    return point;
                }).ToList();

                BKDTree<Point> tree = new(2, Point.CompareDimensionTo, blockSize, parallel);

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

                List<Point> expectedRange = points
                    .Where(point => (!lowerLimit.HasValue || (point.X >= lowerLimit.Value.X && point.Y >= lowerLimit.Value.Y))
                        && (!upperLimit.HasValue || (point.X <= upperLimit.Value.X && point.Y <= upperLimit.Value.Y)))
                    .ToList();

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
                    await Assert.That(hasFirstPoint).IsEqualTo(true);
                    await Assert.That(expectedRange.Contains(firstPoint)).IsEqualTo(true);
                }
                else
                {
                    await Assert.That(hasFirstPoint).IsEqualTo(false);
                }

                await Assert.That(actualRange).IsEquivalentTo(expectedRange);
            }
        }

    }

    public static IEnumerable<object[]> TestCases()
    {
        int[] blockSizes = [2, 3, 4];
        Pattern[] patterns = [
            Pattern.Random,
            Pattern.Const,
        ];
        bool[] parallels = [false, true];
        bool[] bulkInserts = [false, true];
        double?[] limitShares = [null, -1.0, 0.0, 0.5, 1.0, 2.0];

        foreach (int blockSize in blockSizes)
        {
            foreach (Pattern xPattern in patterns)
            {
                foreach (Pattern yPattern in patterns)
                {
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
                                            yield return new object[] { blockSize, xPattern, yPattern, parallel, bulkInsert, lowerLimitShareX, lowerLimitShareY, upperLimitShareX, upperLimitShareY };
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
