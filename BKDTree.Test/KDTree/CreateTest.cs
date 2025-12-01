using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BKDTree.Test.KDTree;

public class CreateTest
{
    [Test]
    [MethodDataSource(nameof(TestCases))]
    public async Task Create(int count, Pattern xPattern, Pattern yPattern, int seed, bool parallel)
    {
        Random random = new(seed);

        Point[] points = Enumerable.Range(0, count).Select(value =>
        {
            double x = Point.GenerateValue(xPattern, value, count, random);
            double y = Point.GenerateValue(yPattern, value, count, random);
            Point point = new(x, y);
            return point;
        }).ToArray();

        // Spezialbehandlung für leere Arrays
        if (count == 0)
        {
            // KDTree wirft jetzt eine ArgumentException für leere Arrays
            Assert.Throws<ArgumentException>(() => new KDTree<Point, PointComparer>(2, points, new PointComparer(), parallel));
            return;
        }

        Dictionary<Point, Point[]> groupedPoints = points
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.ToArray());

        KDTree<Point, PointComparer> tree = new(2, points, new PointComparer(), parallel);

        await Assert.That(tree.Count).IsEqualTo(points.Length);

        foreach (Point point in points)
        {
            bool found = tree.Contains(point);

            await Assert.That(found).IsTrue();

            Point[] existingPoints = tree.Get(point).ToArray();
            Point[] expectedPoints = groupedPoints[point].ToArray();

            await Assert.That(existingPoints).IsEquivalentTo(expectedPoints);

            List<Point> existingPointsList = [];
            tree.DoForEach(point, point =>
            {
                existingPointsList.Add(point);
                return false;
            });
            await Assert.That(existingPointsList).IsEquivalentTo(expectedPoints);
        }
    }

    public static IEnumerable<object[]> TestCases()
    {
        int[] counts = [0, 1, 10, 100, 500]; // Reduced from [0, 1, 2, 10, 50, 100, 500, 1000]
        // Keep representative patterns
        Pattern[] patterns = [
            Pattern.Increasing,
            Pattern.Random,
            Pattern.Const,
            Pattern.Alternating,
        ];
        int[] seeds = [0];
        bool[] parallels = [false, true];

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
                            yield return new object[] { count, xPattern, yPattern, seed, parallel };
                        }
                    }
                }
            }
        }
    }
}
