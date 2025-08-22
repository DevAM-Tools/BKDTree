using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BKDTree.Test;

public class CreateTest
{
    [Test]
    [MethodDataSource(nameof(TestCases))]
    public async Task Create(int blockSize, int count, Pattern xPattern, Pattern yPattern, int seed, bool parallel, bool bulkInsert)
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

        BKDTree<Point> tree = new(2, Point.CompareDimensionTo, blockSize, parallel);
        for (int i = 0; i < count; i++)
        {
            Point point = points[i];
            tree.Insert(point);

            await Assert.That(tree.Count).IsEqualTo(i + 1);
        }

        await Assert.That(tree.Count).IsEqualTo(points.Count);

        if (bulkInsert)
        {
            tree.Insert(points);

            await Assert.That(tree.Count).IsEqualTo(2 * points.Count);
        }

        foreach (Point point in points)
        {
            bool found = tree.Contains(point);

            await Assert.That(found).IsEqualTo(true);

            Point[] expectedPoints = groupedPoints[point].ToArray();

            List<Point> existingPointsList = [];
            tree.DoForEach(point, point =>
            {
                existingPointsList.Add(point);
                return false;
            });
            await Assert.That(existingPointsList).IsEquivalentTo(expectedPoints);

            Point[] existingPoints = tree.Get(point).ToArray();

            await Assert.That(existingPoints).IsEquivalentTo(expectedPoints);
        }
    }

    public static IEnumerable<object[]> TestCases()
    {
        int[] blockSizes = [2, 3, 4];
        int[] counts = [0, 1, 2, 10, 50, 100, 500, 1000];
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
        int[] seeds = [0, 1];
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
                                    yield return new object[] { blockSize, count, xPattern, yPattern, seeds[i], parallel, bulkInsert };
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
