using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BKDTree.Test;
using System;
using System.Linq;

namespace BKDTree.Benchmark;

[EventPipeProfiler(EventPipeProfile.CpuSampling)]
public class Benchmark
{
    [Params(1_000_000)]
    public int N;

    [Params(1024)]
    public int BlockSize;

    [Params(Pattern.Random)]
    public Pattern Pattern;

    public Point[] Points { get; set; }

    public KDTree<Point, PointComparer> KDTree { get; set; }

    public BKDTree<Point, PointComparer> BKDTree { get; set; }

    public Random Random { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Random = new(7);

        Points = Enumerable.Range(0, N).Select(i =>
        {
            double x = Pattern switch
            {
                Pattern.Increasing => i,
                _ => Random.NextDouble()
            };
            double y = Pattern switch
            {
                Pattern.Increasing => i,
                _ => Random.NextDouble()
            };
            Point point = new(x, y);
            return point;
        }).ToArray();

        KDTree = new(2, Points, new PointComparer(), true);

        BKDTree = new(2, new PointComparer(), BlockSize, true);

        foreach (Point point in Points)
        {
            BKDTree.Insert(point);
        }
    }

    [Benchmark(Baseline = true)]
    public void CreateKd()
    {
        KDTree<Point, PointComparer> kdTree = new(2, Points, new PointComparer(), false);
    }

    [Benchmark]
    public void CreateKdParallel()
    {
        KDTree<Point, PointComparer> kdTree = new(2, Points, new PointComparer(), true);
    }

    [Benchmark]
    public void CreateBkd()
    {
        BKDTree<Point, PointComparer> bkdTree = new(2, new PointComparer(), BlockSize, false);
        foreach (Point point in Points)
        {
            bkdTree.Insert(point);
        }
    }

    [Benchmark]
    public void CreateBkdParallel()
    {
        BKDTree<Point, PointComparer> bkdTree = new(2, new PointComparer(), BlockSize, true);
        foreach (Point point in Points)
        {
            bkdTree.Insert(point);
        }
    }

    [Benchmark]
    public void GetKd()
    {
        int index = Random.Next(Points.Length);
        Point point = Points[index];
        KDTree.DoForEach(point, static point => false);
    }

    [Benchmark]
    public void GetBkd()
    {
        int index = Random.Next(Points.Length);
        Point point = Points[index];
        BKDTree.DoForEach(point, static point => false);
    }

    [Benchmark]
    public void QuerySmallKd()
    {
        int count = 0;
        Point lowerLimit = new(0.45, 0.45);
        Point upperLimit = new(0.55, 0.55);

        KDTree.DoForEach(point =>
        {
            count++;
            return false;
        }, lowerLimit, upperLimit, true);
    }

    [Benchmark]
    public void QuerySmallBkd()
    {
        int count = 0;
        Point lowerLimit = new(0.45, 0.45);
        Point upperLimit = new(0.55, 0.55);

        BKDTree.DoForEach(point =>
        {
            count++;
            return false;
        }, lowerLimit, upperLimit, true);
    }

    [Benchmark]
    public void QueryLargeKd()
    {
        int count = 0;
        Point lowerLimit = new(0.25, 0.25);
        Point upperLimit = new(0.75, 0.75);

        KDTree.DoForEach(point =>
        {
            count++;
            return false;
        }, lowerLimit, upperLimit, true);
    }

    [Benchmark]
    public void QueryLargeBkd()
    {
        int count = 0;
        Point lowerLimit = new(0.25, 0.25);
        Point upperLimit = new(0.75, 0.75);

        BKDTree.DoForEach(point =>
        {
            count++;
            return false;
        }, lowerLimit, upperLimit, true);
    }

    [Benchmark]
    public void ManualQuerySmall()
    {
        int count = 0;
        Point lowerLimit = new(0.45, 0.45);
        Point upperLimit = new(0.55, 0.55);

        foreach (Point point in Points)
        {
            if (point.X >= lowerLimit.X && point.X <= upperLimit.X && point.Y >= lowerLimit.Y && point.Y <= upperLimit.Y)
            {
                count++;
            }
        }
    }

}