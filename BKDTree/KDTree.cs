using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class KDTree<T, TComparer>
    where T : notnull
    where TComparer : struct, IDimensionalComparer<T>
{
    /// <summary>
    /// Minimum number of elements in a subtree before parallel construction is considered.
    /// Below this threshold, the overhead of thread management outweighs parallelization benefits.
    /// Benchmarks show that 4096 provides better performance than lower values due to reduced
    /// thread coordination overhead.
    /// </summary>
    internal const int ParallelConstructionThreshold = 4096;

    /// <summary>
    /// Maximum theoretical recursion depth for tree operations.
    /// For a balanced tree with N elements, the maximum depth is log2(N).
    /// With 2^31 elements (int.MaxValue), max depth is 31 levels - well within stack limits.
    /// The tree construction ensures balance by using median partitioning.
    /// </summary>
    internal const int MaxTheoreticalDepth = 31;

    internal readonly int DimensionCount;
    internal readonly T[] Values;
    internal readonly bool[] Dirties;
    internal readonly TComparer Comparer;

    public int Count => Values.Length;

    // Thread count stored in array to allow Interlocked operations without boxing
    // Using array instead of class reduces allocations
    private static int[] CreateThreadCounter() => [1];

    public KDTree(int dimensionCount, IEnumerable<T> values, TComparer comparer, bool parallel = false)
        : this(dimensionCount, values, comparer, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public KDTree(int dimensionCount, IEnumerable<T> values, TComparer comparer, int maxThreadCount)
    {
        if (dimensionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionCount));
        }

        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        Comparer = comparer;
        DimensionCount = dimensionCount;
        // Avoid unnecessary array allocation if values is already an array
        Values = values as T[] ?? values.ToArray();

        if (Values.Length == 0)
        {
            throw new ArgumentException("Values collection cannot be empty.", nameof(values));
        }

        Dirties = new bool[Values.Length];

        DimensionalComparer<T, TComparer>[] comparers = new DimensionalComparer<T, TComparer>[DimensionCount];
        for (int dimension = 0; dimension < DimensionCount; dimension++)
        {
            comparers[dimension] = new(dimension, Comparer);
        }

        int[] threadCount = CreateThreadCounter();
        maxThreadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, maxThreadCount));
        Build(0, Values.Length - 1, 0, comparers, threadCount, maxThreadCount);
    }

    internal KDTree(int dimensionCount, IList<Segment<T>> values, TComparer comparer, DimensionalComparer<T, TComparer>[] comparers, bool parallel = false)
        : this(dimensionCount, values, comparer, comparers, parallel ? Environment.ProcessorCount : 1)
    {
    }

    internal KDTree(int dimensionCount, IList<Segment<T>> values, TComparer comparer, DimensionalComparer<T, TComparer>[] comparers, int maxThreadCount)
    {
        if (dimensionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionCount));
        }

        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Count == 0)
        {
            throw new ArgumentException("Values collection cannot be empty.", nameof(values));
        }

        Comparer = comparer;
        DimensionCount = dimensionCount;

        long count = 0L;
        for (int i = 0; i < values.Count; i++)
        {
            Segment<T> segment = values[i];
            if (!(segment.Values is T[] || segment.Values is List<T>))
            {
                throw new ArgumentException("Segment must be either of type T[] or List<T>.");
            }
            count += segment.Length;
        }

        Values = new T[count];
        Dirties = new bool[count];

        maxThreadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, maxThreadCount));
        
        // Pre-compute segment start indices
        int[] segmentIndices = new int[values.Count];
        int currentIndex = 0;
        for (int i = 0; i < values.Count; i++)
        {
            segmentIndices[i] = currentIndex;
            currentIndex += values[i].Length;
        }

        if (maxThreadCount > 1 && values.Count > 1)
        {
            // Use Parallel.For instead of PLINQ for less allocation overhead
            Parallel.For(0, values.Count, new ParallelOptions { MaxDegreeOfParallelism = maxThreadCount }, i =>
            {
                Segment<T> segment = values[i];
                int targetIndex = segmentIndices[i];
                if (segment.Values is T[] array)
                {
                    Array.Copy(array, segment.Offset, Values, targetIndex, segment.Length);
                }
                else if (segment.Values is List<T> list)
                {
                    list.CopyTo(segment.Offset, Values, targetIndex, segment.Length);
                }
            });
        }
        else
        {
            for (int i = 0; i < values.Count; i++)
            {
                Segment<T> segment = values[i];
                int targetIndex = segmentIndices[i];
                if (segment.Values is T[] array)
                {
                    Array.Copy(array, segment.Offset, Values, targetIndex, segment.Length);
                }
                else if (segment.Values is List<T> list)
                {
                    list.CopyTo(segment.Offset, Values, targetIndex, segment.Length);
                }
            }
        }

        int[] threadCount = CreateThreadCounter();
        Build(0, Values.Length - 1, 0, comparers, threadCount, maxThreadCount);
    }

    private void Build(int leftIndex, int rightIndex, int dimension, DimensionalComparer<T, TComparer>[] comparers, int[] threadCount, int maxThreadCount)
    {
        int count = rightIndex + 1 - leftIndex;

        DimensionalComparer<T, TComparer> comparer = comparers[dimension];
        Array.Sort(Values, Dirties, leftIndex, count, comparer);

        int midIndex = (rightIndex + leftIndex) / 2;
        ref T midValue = ref Values[midIndex];
        ref bool midDirty = ref Dirties[midIndex];
        int index = FindFirstIndexOf(ref midValue, leftIndex, rightIndex, dimension);

        midDirty = index < midIndex;

        int nextDimension = NextDimension(dimension);

        int leftNextLeftIndex = leftIndex;
        int leftNextRightIndex = midIndex - 1;

        Task leftTask = null;
        if (leftNextRightIndex >= leftNextLeftIndex)
        {
            int nextSpanSize = leftNextRightIndex - leftNextLeftIndex;
            if (maxThreadCount > 1 && nextSpanSize >= ParallelConstructionThreshold && DoInParallel(threadCount, maxThreadCount))
            {
                leftTask = Task.Run(() =>
                {
                    Build(leftNextLeftIndex, leftNextRightIndex, nextDimension, comparers, threadCount, maxThreadCount);
                });
            }
            else
            {
                Build(leftNextLeftIndex, leftNextRightIndex, nextDimension, comparers, threadCount, maxThreadCount);
            }
        }

        int rightNextLeftIndex = midIndex + 1;
        int rightNextRightIndex = rightIndex;

        Task rightTask = null;
        if (rightNextRightIndex >= rightNextLeftIndex)
        {
            int nextSpanSize = rightNextRightIndex - rightNextLeftIndex;
            if (maxThreadCount > 1 && nextSpanSize >= ParallelConstructionThreshold && DoInParallel(threadCount, maxThreadCount))
            {
                rightTask = Task.Run(() =>
                {
                    Build(rightNextLeftIndex, rightNextRightIndex, nextDimension, comparers, threadCount, maxThreadCount);
                });
            }
            else
            {
                Build(rightNextLeftIndex, rightNextRightIndex, nextDimension, comparers, threadCount, maxThreadCount);
            }
        }

        if (leftTask is not null)
        {
            leftTask.Wait();
            Interlocked.Decrement(ref threadCount[0]);
        }
        if (rightTask is not null)
        {
            rightTask.Wait();
            Interlocked.Decrement(ref threadCount[0]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool DoInParallel(int[] threadCount, int maxThreadCount)
    {
        while (true)
        {
            int currentThreadCount = Volatile.Read(ref threadCount[0]);
            if (currentThreadCount >= maxThreadCount)
            {
                return false;
            }

            int previousThreadCount = Interlocked.CompareExchange(ref threadCount[0], currentThreadCount + 1, currentThreadCount);
            if (previousThreadCount == currentThreadCount)
            {
                return true;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindFirstIndexOf(ref T value, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = leftIndex;
        int compareResult = 0;

        while (rightIndex >= leftIndex)
        {
            midIndex = (rightIndex + leftIndex) / 2;
            ref T currentValue = ref Values[midIndex];
            compareResult = Comparer.Compare(in value, in currentValue, dimension);

            if (compareResult < 0)
            {
                if (rightIndex == midIndex)
                {
                    break;
                }
                rightIndex = midIndex;
            }
            else if (compareResult > 0)
            {
                leftIndex = midIndex + 1;
            }
            else
            {
                // recursively search for matches on the left side.
                int index = FindFirstIndexOf(ref value, leftIndex, midIndex - 1, dimension);
                if (index >= 0 && index < midIndex)
                {
                    return index;
                }

                break;
            }
        }

        if (compareResult > 0)
        {
            return -1;
        }
        else
        {
            return midIndex;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsEqualTo(in T left, in T right)
    {
        int dimensionCount = DimensionCount;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (Comparer.Compare(in left, in right, dimension) != 0)
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsKeyLessThanOrEqualToLimit(in T value, in T limit)
    {
        int dimensionCount = DimensionCount;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (Comparer.Compare(in value, in limit, dimension) > 0)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsKeyLessThanLimit(in T value, in T limit)
    {
        int dimensionCount = DimensionCount;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (Comparer.Compare(in value, in limit, dimension) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsKeyGreaterThanOrEqualToLimit(in T value, in T limit)
    {
        int dimensionCount = DimensionCount;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (Comparer.Compare(in value, in limit, dimension) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Calculates the next dimension index with wraparound.
    /// Optimized for common cases (especially 2D where it becomes dimension ^ 1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int NextDimension(int dimension)
    {
        int next = dimension + 1;
        return next == DimensionCount ? 0 : next;
    }

    /// <summary>
    /// Gets all values. Consider using <see cref="DoForEach(Action{T})"/> ot <see cref="DoForEach(Func{T, bool})"/> if performance is critical.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<T> GetAll() => Values;

    /// <summary>
    /// Gets an allocation-free enumerable over all values.
    /// Can be used with foreach but cannot be stored or used with async/await.
    /// </summary>
    /// <returns>A ref struct enumerable for allocation-free iteration.</returns>
    public ValueEnumerable<T> GetAllFast() => new(Values);

    /// <summary>
    /// Performs an <paramref name="action"/> for every value. Prefer this over <see cref="GetAll()"/> in performance critical paths.
    /// </summary>
    /// <param name="action">Will be called for every value.</param>
    public void DoForEach(Action<T> action)
    {
        if (action is null)
        {
            return;
        }
        ActionWrapper<T> wrapper = new(action);
        DoForEach(ref wrapper);
    }

    /// <summary>
    /// Applies a <paramref name="actionAndCancelFunction"/> to every value and allows cancellation of the iteration. Prefer this over <see cref="GetAll()"/> in performance critical paths.
    /// </summary>
    /// <param name="actionAndCancelFunction">Will be called for every value. If it returns true the iteration will be canceled.</param>
    /// <returns>true if the iteration was canceled otherwise false</returns>
    public bool DoForEach(Func<T, bool> actionAndCancelFunction)
    {
        if (actionAndCancelFunction is null)
        {
            return false;
        }
        FuncWrapper<T> wrapper = new(actionAndCancelFunction);
        return DoForEach(ref wrapper);
    }

    /// <summary>
    /// Applies a struct callback to every value with cancellation support for maximum performance.
    /// The struct callback eliminates delegate allocation and allows full JIT inlining.
    /// </summary>
    /// <typeparam name="TFunc">The struct type implementing IForEachFunc</typeparam>
    /// <param name="func">The struct callback that will be invoked for every value.</param>
    /// <returns>true if the iteration was canceled otherwise false</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DoForEach<TFunc>(ref TFunc func)
        where TFunc : struct, IForEachFunc<T>
    {
        T[] values = Values;
        for (int i = 0; i < values.Length; i++)
        {
            if (func.Invoke(values[i]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Applies a struct callback with ref readonly parameter to every value with cancellation support.
    /// Use this for large structs to avoid copying. The struct callback eliminates delegate allocation.
    /// </summary>
    /// <typeparam name="TFunc">The struct type implementing IForEachRefFunc</typeparam>
    /// <param name="func">The struct callback that will be invoked for every value.</param>
    /// <returns>true if the iteration was canceled otherwise false</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DoForEachRef<TFunc>(ref TFunc func)
        where TFunc : struct, IForEachRefFunc<T>
    {
        T[] values = Values;
        for (int i = 0; i < values.Length; i++)
        {
            if (func.Invoke(in values[i]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets all matching values. Since <see cref="KDTree{T}"/> does allow duplicates this may be more than one. Consider using <see cref="DoForEach(T, Func{T, bool})"/> if performance is critical.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public IEnumerable<T> Get(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        IEnumerable<T> result = Get(value, 0, Count - 1, 0);
        return result;
    }

    internal IEnumerable<T> Get(T value, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        T midValue = Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (IsEqualTo(in value, in midValue))
        {
            yield return midValue;
        }

        int nextDimension = NextDimension(dimension);
        int comparisonResult = Comparer.Compare(in value, in midValue, dimension);

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                foreach (T currentValue in Get(value, nextLeftIndex, nextRightIndex, nextDimension))
                {
                    yield return currentValue;
                }
            }
        }

        if (comparisonResult < 0 || (dirty && comparisonResult == 0))
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                foreach (T currentValue in Get(value, nextLeftIndex, nextRightIndex, nextDimension))
                {
                    yield return currentValue;
                }
            }
        }
    }

    /// <summary>
    /// Applies an <paramref name="actionAndCancelFunction"/> to every matching values. Since <see cref="KDTree{T}"/> does allow duplicates this may be more than one. Prefer this over <see cref="Get(T)"/> in performance critical paths.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="actionAndCancelFunction">Will be called for every matching value. If it returns true the iteration will be canceled.</param>
    /// <returns>true if the iteration was canceled otherwise false</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public bool DoForEach(T value, Func<T, bool> actionAndCancelFunction)
    {
        if (actionAndCancelFunction is null)
        {
            return false;
        }
        FuncWrapper<T> wrapper = new(actionAndCancelFunction);
        return DoForEach(value, ref wrapper);
    }

    /// <summary>
    /// Applies a struct callback to every matching value with cancellation support for maximum performance.
    /// The struct callback eliminates delegate allocation and allows full JIT inlining.
    /// </summary>
    /// <typeparam name="TFunc">The struct type implementing IForEachFunc</typeparam>
    /// <param name="value">The value to search for</param>
    /// <param name="func">The struct callback that will be invoked for every matching value.</param>
    /// <returns>true if the iteration was canceled otherwise false</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public bool DoForEach<TFunc>(T value, ref TFunc func)
        where TFunc : struct, IForEachFunc<T>
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return DoForEachInternal(value, ref func, 0, Count - 1, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool DoForEachInternal<TFunc>(T value, ref TFunc func, int leftIndex, int rightIndex, int dimension)
        where TFunc : struct, IForEachFunc<T>
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (IsEqualTo(in value, in midValue))
        {
            if (func.Invoke(midValue))
            {
                return true;
            }
        }

        int nextDimension = NextDimension(dimension);
        int comparisonResult = Comparer.Compare(in value, in midValue, dimension);

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                if (DoForEachInternal(value, ref func, nextLeftIndex, nextRightIndex, nextDimension))
                {
                    return true;
                }
            }
        }

        if (comparisonResult < 0 || (dirty && comparisonResult == 0))
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                if (DoForEachInternal(value, ref func, nextLeftIndex, nextRightIndex, nextDimension))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Applies an <paramref name="actionAndCancelFunction"/> for every single item within an optional inclusive <paramref name="lowerLimit"/> and an optional <paramref name="upperLimit"/>. 
    /// The upper limit is inclusive if <paramref name="upperLimitInclusive"/> is true otherwise the upper limit is exclusive.
    /// </summary>
    /// <param name="actionAndCancelFunction">Will be called for every matching value. If it returns true the iteration will be canceled.</param>
    /// <param name="lowerLimit">Optional inclusive lower limit</param>
    /// <param name="upperLimit">Optional upper limit</param>
    /// <param name="upperLimitInclusive">The upper limit is inclusive if true otherwise the upper limit is exclusive</param>
    /// <returns>true if the operation was canceled otherwise false</returns>
    public bool DoForEach(Func<T, bool> actionAndCancelFunction, Option<T> lowerLimit, Option<T> upperLimit, bool upperLimitInclusive)
    {
        if (actionAndCancelFunction is null)
        {
            return false;
        }
        FuncWrapper<T> wrapper = new(actionAndCancelFunction);
        return DoForEach(ref wrapper, lowerLimit, upperLimit, upperLimitInclusive);
    }

    /// <summary>
    /// Applies a struct callback for every single item within an optional inclusive lower limit and an optional upper limit.
    /// The struct callback eliminates delegate allocation and allows full JIT inlining.
    /// </summary>
    /// <typeparam name="TFunc">The struct type implementing IForEachFunc</typeparam>
    /// <param name="func">The struct callback that will be invoked for every matching value.</param>
    /// <param name="lowerLimit">Optional inclusive lower limit</param>
    /// <param name="upperLimit">Optional upper limit</param>
    /// <param name="upperLimitInclusive">The upper limit is inclusive if true otherwise the upper limit is exclusive</param>
    /// <returns>true if the operation was canceled otherwise false</returns>
    public bool DoForEach<TFunc>(ref TFunc func, Option<T> lowerLimit, Option<T> upperLimit, bool upperLimitInclusive)
        where TFunc : struct, IForEachFunc<T>
    {
        if (lowerLimit.HasValue && upperLimit.HasValue)
        {
            int dimensionCount = DimensionCount;
            for (int dimension = 0; dimension < dimensionCount; dimension++)
            {
                T lowerVal = lowerLimit.Value;
                T upperVal = upperLimit.Value;
                int comparisonResult = Comparer.Compare(in lowerVal, in upperVal, dimension);

                if (comparisonResult > 0)
                {
                    return false;
                }
            }
        }

        return DoForEachInternal(ref func, ref lowerLimit, ref upperLimit, upperLimitInclusive, 0, Count - 1, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool DoForEachInternal<TFunc>(ref TFunc func, ref Option<T> lowerLimit, ref Option<T> upperLimit, bool upperLimitInclusive, int leftIndex, int rightIndex, int dimension)
        where TFunc : struct, IForEachFunc<T>
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (!lowerLimit.HasValue || IsKeyGreaterThanOrEqualToLimit(midValue, lowerLimit.Value))
        {
            if (upperLimitInclusive)
            {
                if (!upperLimit.HasValue || IsKeyLessThanOrEqualToLimit(midValue, upperLimit.Value))
                {
                    if (func.Invoke(midValue))
                    {
                        return true;
                    }
                }
            }
            else
            {
                if (!upperLimit.HasValue || IsKeyLessThanLimit(midValue, upperLimit.Value))
                {
                    if (func.Invoke(midValue))
                    {
                        return true;
                    }
                }
            }
        }

        int nextDimension = NextDimension(dimension);

        int upperLimitComparisonResult;
        if (upperLimit.HasValue)
        {
            T upperVal = upperLimit.Value;
            upperLimitComparisonResult = Comparer.Compare(in upperVal, in midValue, dimension);
        }
        else
        {
            upperLimitComparisonResult = int.MaxValue;
        }

        if (upperLimitComparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                if (DoForEachInternal(ref func, ref lowerLimit, ref upperLimit, upperLimitInclusive, nextLeftIndex, nextRightIndex, nextDimension))
                {
                    return true;
                }
            }
        }

        int lowerLimitComparisonResult;
        if (lowerLimit.HasValue)
        {
            T lowerVal = lowerLimit.Value;
            lowerLimitComparisonResult = Comparer.Compare(in lowerVal, in midValue, dimension);
        }
        else
        {
            lowerLimitComparisonResult = int.MinValue;
        }

        if (lowerLimitComparisonResult <= 0 || (dirty && upperLimitComparisonResult == 0))
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                if (DoForEachInternal(ref func, ref lowerLimit, ref upperLimit, upperLimitInclusive, nextLeftIndex, nextRightIndex, nextDimension))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if at least one matching item is contained.
    /// </summary>
    /// <param name="value"></param>
    /// <returns>true if at least one matching item is contained otherwise false</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public bool Contains(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        bool contains = Contains(value, 0, Count - 1, 0);
        return contains;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(T value, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (IsEqualTo(in value, in midValue))
        {
            return true;
        }

        int nextDimension = NextDimension(dimension);
        int comparisonResult = Comparer.Compare(in value, in midValue, dimension);

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            if (rightIndex >= nextLeftIndex && Contains(value, nextLeftIndex, rightIndex, nextDimension))
            {
                return true;
            }
        }

        if (comparisonResult < 0 || (dirty && comparisonResult == 0))
        {
            int nextRightIndex = midIndex - 1;
            if (nextRightIndex >= leftIndex && Contains(value, leftIndex, nextRightIndex, nextDimension))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to retrieve the first element within an optional inclusive <paramref name="lowerLimit"/> and an optional <paramref name="upperLimit"/>. 
    /// The upper limit is inclusive if <paramref name="upperLimitInclusive"/> is true otherwise the upper limit is exclusive.
    /// </summary>
    /// <param name="lowerLimit">Optional inclusive lower limit</param>
    /// <param name="upperLimit">Optional upper limit</param>
    /// <param name="upperLimitInclusive">The upper limit is inclusive if true otherwise the upper limit is exclusive</param>
    /// <param name="value">The first element within the limits</param>
    /// <returns>true if an element is found otherwise false</returns>
    public bool TryGetFirst(Option<T> lowerLimit, Option<T> upperLimit, bool upperLimitInclusive, ref T value)
    {
        if (lowerLimit.HasValue && upperLimit.HasValue)
        {
            for (int dimension = 0; dimension < DimensionCount; dimension++)
            {
                T lowerVal = lowerLimit.Value;
                T upperVal = upperLimit.Value;
                int comparisonResult = Comparer.Compare(in lowerVal, in upperVal, dimension);

                if (comparisonResult > 0)
                {
                    return false;
                }
            }
        }

        return TryGetFirst(ref lowerLimit, ref upperLimit, upperLimitInclusive, 0, Count - 1, 0, ref value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetFirst(ref Option<T> lowerLimit, ref Option<T> upperLimit, bool upperLimitInclusive, int leftIndex, int rightIndex, int dimension, ref T value)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (!lowerLimit.HasValue || IsKeyGreaterThanOrEqualToLimit(midValue, lowerLimit.Value))
        {
            if (upperLimitInclusive)
            {
                if (!upperLimit.HasValue || IsKeyLessThanOrEqualToLimit(midValue, upperLimit.Value))
                {
                    value = midValue;
                    return true;
                }
            }
            else
            {
                if (!upperLimit.HasValue || IsKeyLessThanLimit(midValue, upperLimit.Value))
                {
                    value = midValue;
                    return true;
                }
            }
        }

        int nextDimension = NextDimension(dimension);

        // Use int with sentinel instead of nullable to avoid boxing
        int upperLimitComparisonResult;
        if (upperLimit.HasValue)
        {
            T upperVal = upperLimit.Value;
            upperLimitComparisonResult = Comparer.Compare(in upperVal, in midValue, dimension);
        }
        else
        {
            upperLimitComparisonResult = int.MaxValue;
        }

        if (upperLimitComparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                bool found = TryGetFirst(ref lowerLimit, ref upperLimit, upperLimitInclusive, nextLeftIndex, nextRightIndex, nextDimension, ref value);
                if (found)
                {
                    return true;
                }
            }
        }

        int lowerLimitComparisonResult;
        if (lowerLimit.HasValue)
        {
            T lowerVal = lowerLimit.Value;
            lowerLimitComparisonResult = Comparer.Compare(in lowerVal, in midValue, dimension);
        }
        else
        {
            lowerLimitComparisonResult = int.MinValue;
        }

        if (lowerLimitComparisonResult <= 0 || (dirty && upperLimitComparisonResult == 0))
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                bool found = TryGetFirst(ref lowerLimit, ref upperLimit, upperLimitInclusive, nextLeftIndex, nextRightIndex, nextDimension, ref value);
                if (found)
                {
                    return true;
                }
            }
        }

        return false;
    }

}