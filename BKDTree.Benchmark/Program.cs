using BKDTree.Test;
using System.Collections.Generic;
using BenchmarkDotNet.Jobs;

namespace BKDTree.Benchmark;

internal class Program
{
    private static void Main(string[] args)
    {
#if true
        BenchmarkDotNet.Configs.ManualConfig config = BenchmarkDotNet.Configs.ManualConfig.CreateMinimumViable()
            .AddJob(Job.ShortRun.WithEvaluateOverhead(false));

        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
#else
        int count = 10_000_000;
        List<Point> points = [];
        System.Random random = new(7);
        for (int i = 0; i < count; i++)
        {
            double x = random.NextDouble();
            double y = random.NextDouble();
            Point point = new(x, y);
            points.Add(point);
        }

        BKDTree<Point> tree = new(2, parallel: true);

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        tree.Insert(points);

        watch.Stop();
        System.Console.WriteLine($"Duration: {watch.Elapsed}");
#endif
    }
}