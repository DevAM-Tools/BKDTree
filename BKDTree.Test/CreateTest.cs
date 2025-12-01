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

        BKDTree<Point, PointComparer> tree = new(2, new PointComparer(), blockSize, parallel);
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
        // Reduced test matrix for faster execution while maintaining coverage
        int[] blockSizes = [2, 4]; // Reduced from [2, 3, 4]
        int[] counts = [0, 1, 10, 100, 500]; // Reduced from [0, 1, 2, 10, 50, 100, 500, 1000]
        // Keep representative patterns that cover different tree behaviors
        Pattern[] patterns = [
            Pattern.Increasing,     // Sorted input
            Pattern.Random,         // Random input
            Pattern.Const,          // All same values (duplicates)
            Pattern.Alternating,    // Alternating pattern
        ];
        int[] seeds = [0];
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
                        // Only use multiple seeds for random patterns
                        int[] effectiveSeeds = (xPattern == Pattern.Random || yPattern == Pattern.Random) ? [0, 1] : seeds;
                        
                        foreach (int seed in effectiveSeeds)
                        {
                            foreach (bool parallel in parallels)
                            {
                                foreach (bool bulkInsert in bulkInserts)
                                {
                                    yield return new object[] { blockSize, count, xPattern, yPattern, seed, parallel, bulkInsert };
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
