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
            Assert.Throws<ArgumentException>(() => new KDTree<Point>(2, points, Point.CompareDimensionTo, parallel));
            return;
        }

        Dictionary<Point, Point[]> groupedPoints = points
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.ToArray());

        KDTree<Point> tree = new(2, points, Point.CompareDimensionTo, parallel);

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
                            yield return new object[] { count, xPattern, yPattern, seeds[i], parallel };
                        }

                    }
                }
            }
        }
    }
}
