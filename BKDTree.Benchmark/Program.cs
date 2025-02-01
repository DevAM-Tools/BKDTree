using BKDTree.Test;

namespace BKDTree.Benchmark;

internal class Program
{
    private static void Main(string[] args)
    {
#if false
        BenchmarkDotNet.Configs.ManualConfig config = BenchmarkDotNet.Configs.ManualConfig.CreateMinimumViable()
            .AddJob(Job.ShortRun.WithEvaluateOverhead(false));

        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
#else
        int count = 10_000_000;
        Point[] points = new Point[count];
        System.Random random = new(7);
        for (int i = 0; i < points.Length; i++)
        {
            double x = random.NextDouble();
            double y = random.NextDouble();
            Point point = new(x, y);
            points[i] = point;
        }

        BKDTree<Point> tree = new(2, parallel: true);

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        foreach (Point point in points)
        {
            tree.Insert(point);
        }

        watch.Stop();
        System.Console.WriteLine($"Duration: {watch.Elapsed}");
#endif
    }
}