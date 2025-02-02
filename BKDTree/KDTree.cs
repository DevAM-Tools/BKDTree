using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class KDTree<T> where T : ITreeItem<T>
{
    internal readonly int DimensionCount;
    internal readonly T[] Values;
    internal readonly bool[] Dirties;

    public int Count => Values.Length;

    private class Box(int value)
    {
        public volatile int Value = value;
    }

    public KDTree(int dimensionCount, IEnumerable<T> values, bool parallel = false)
        : this(dimensionCount, values, parallel ? Environment.ProcessorCount : 1)
    {
    }
    public KDTree(int dimensionCount, IEnumerable<T> values, int maxThreadCount)
    {
        if (dimensionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionCount));
        }

        DimensionCount = dimensionCount;
        Values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
        Dirties = new bool[Values.Length];

        DimensionalComparer<T>[] comparers = new DimensionalComparer<T>[DimensionCount];
        for (int dimension = 0; dimension < DimensionCount; dimension++)
        {
            comparers[dimension] = new(dimension);
        }

        Box threadCount = new(1);
        maxThreadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, maxThreadCount));
        Build(0, Values.Length - 1, 0, comparers, threadCount, maxThreadCount);
    }

    internal KDTree(int dimensionCount, T[][] values, DimensionalComparer<T>[] comparers, bool parallel = false)
        : this(dimensionCount, values, comparers, parallel ? Environment.ProcessorCount : 1)
    {
    }

    internal KDTree(int dimensionCount, T[][] values, DimensionalComparer<T>[] comparers, int maxThreadCount)
    {
        if (dimensionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionCount));
        }

        DimensionCount = dimensionCount;

        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        long count = 0L;
        for (int i = 0; i < values.Length; i++)
        {
            T[] currentValues = values[i] ?? throw new ArgumentNullException(nameof(values));
            count += currentValues.Length;
        }

        Values = new T[count];
        Dirties = new bool[count];

        maxThreadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, maxThreadCount));
        if (maxThreadCount > 1)
        {
            IEnumerable<(int I, int Index)> GetIndex()
            {
                int index = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    yield return (i, index);
                    T[] currentValues = values[i] ?? throw new ArgumentNullException(nameof(values));
                    index += currentValues.Length;
                }
            }

            GetIndex()
                .AsParallel()
                .WithDegreeOfParallelism(maxThreadCount)
                .ForAll(parameter =>
                {
                    T[] currentValues = values[parameter.I];
                    Array.Copy(currentValues, 0, Values, parameter.Index, currentValues.Length);
                });
        }
        else
        {
            int index = 0;
            for (int i = 0; i < values.Length; i++)
            {
                T[] currentValues = values[i];
                Array.Copy(currentValues, 0, Values, index, currentValues.Length);
                index += currentValues.Length;
            }
        }

        Box threadCount = new(1);
        Build(0, Values.Length - 1, 0, comparers, threadCount, maxThreadCount);
    }

    private void Build(int leftIndex, int rightIndex, int dimension, DimensionalComparer<T>[] comparers, Box threadCount, int maxThreadCount)
    {
        int count = rightIndex + 1 - leftIndex;

        DimensionalComparer<T> comparer = comparers[dimension];
        Array.Sort(Values, Dirties, leftIndex, count, comparer);

        int midIndex = (rightIndex + leftIndex) / 2;
        ref T midValue = ref Values[midIndex];
        ref bool midDirty = ref Dirties[midIndex];
        int index = FindFirstIndexOf(ref midValue, leftIndex, rightIndex, dimension);

        midDirty = index < midIndex;

        int nextDimension = (dimension + 1) % DimensionCount;

        int leftNextLeftIndex = leftIndex;
        int leftNextRightIndex = midIndex - 1;

        Task leftTask = null;
        if (leftNextRightIndex >= leftNextLeftIndex)
        {
            int nextSpanSize = leftNextRightIndex - leftNextLeftIndex;
            if (maxThreadCount > 0 && nextSpanSize >= 512 && DoInParallel(threadCount, maxThreadCount))
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
            if (maxThreadCount > 0 && nextSpanSize >= 512 && DoInParallel(threadCount, maxThreadCount))
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
            Interlocked.Decrement(ref threadCount.Value);
        }
        if (rightTask is not null)
        {
            rightTask.Wait();
            Interlocked.Decrement(ref threadCount.Value);
        }
    }

    private bool DoInParallel(Box threadCount, int maxThreadCount)
    {
        int currentThreadCount = 0;
        while (true)
        {
            currentThreadCount = threadCount.Value;
            if (currentThreadCount >= maxThreadCount)
            {
                return false;
            }

            int previousThreadCount = Interlocked.CompareExchange(ref threadCount.Value, currentThreadCount + 1, currentThreadCount);
            if (previousThreadCount == currentThreadCount)
            {
                return true;
            }
        }
    }

    private int FindFirstIndexOf(ref T value, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = leftIndex;
        int compareResult = 0;

        while (rightIndex >= leftIndex)
        {
            midIndex = (rightIndex + leftIndex) / 2;
            ref T currentValue = ref Values[midIndex];
            compareResult = value.CompareDimensionTo(currentValue, dimension);

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
    internal static bool IsEqualTo(T left, T right, int dimensionCount)
    {
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (left.CompareDimensionTo(right, dimension) != 0)
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsKeyLessThanOrEqualToLimit(T value, T limit, int dimensionCount)
    {
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (value.CompareDimensionTo(limit, dimension) > 0)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsKeyLessThanLimit(T value, T limit, int dimensionCount)
    {
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (value.CompareDimensionTo(limit, dimension) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsKeyGreaterThanOrEqualToLimit(T value, T limit, int dimensionCount)
    {
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            if (value.CompareDimensionTo(limit, dimension) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets all values. Consider using <see cref="DoForEach(Action{T})"/> ot <see cref="DoForEach(Func{T, bool})"/> if performance is critical.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<T> GetAll()
    {
        foreach (T value in Values)
        {
            yield return value;
        }
    }

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

        foreach (T value in Values)
        {
            action.Invoke(value);
        }
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

        foreach (T value in Values)
        {
            bool cancel = actionAndCancelFunction.Invoke(value);
            if (cancel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all matching values. Since <see cref="KDTree{T}"/> does allow duplicates this may be more than one. Consider using <see cref="DoForEach(T, Func{T, bool}"/> if performance is critical.
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

        if (IsEqualTo(value, midValue, DimensionCount))
        {
            yield return midValue;
        }

        int nextDimension = (dimension + 1) % DimensionCount;
        int comparisonResult = value.CompareDimensionTo(midValue, dimension);

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
    /// Applies an <paramref name="actionAndCancelFunction"/> to every mathing values. Since <see cref="KDTree{T}"/> does allow duplicates this may be more than one. Prefer this over <see cref="Get(T)"/> in performance critical paths.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="actionAndCancelFunction">Will be called for every matching value. If it returns true the iteration will be canceled.</param>
    /// <returns>true if the iteration was canceled otherwise false</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public bool DoForEach(T value, Func<T, bool> actionAndCancelFunction)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        if (actionAndCancelFunction is null)
        {
            return false;
        }

        return DoForEach(value, actionAndCancelFunction, 0, Count - 1, 0);
    }

    internal bool DoForEach(T value, Func<T, bool> actionAndCancelFunction, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (IsEqualTo(value, midValue, DimensionCount))
        {
            bool cancel = actionAndCancelFunction.Invoke(midValue);
            if (cancel)
            {
                return true;
            }
        }

        int nextDimension = (dimension + 1) % DimensionCount;
        int comparisonResult = value.CompareDimensionTo(midValue, dimension);

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                bool cancel = DoForEach(value, actionAndCancelFunction, nextLeftIndex, nextRightIndex, nextDimension);
                if (cancel)
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
                bool cancel = DoForEach(value, actionAndCancelFunction, nextLeftIndex, nextRightIndex, nextDimension);
                if (cancel)
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

        if (lowerLimit.HasValue && upperLimit.HasValue)
        {
            for (int dimension = 0; dimension < DimensionCount; dimension++)
            {
                int comparisonResult = lowerLimit.Value.CompareDimensionTo(upperLimit.Value, dimension);

                if (comparisonResult > 0)
                {
                    return false;
                }
            }
        }

        return DoForEach(actionAndCancelFunction, ref lowerLimit, ref upperLimit, upperLimitInclusive, 0, Count - 1, 0);
    }

    internal bool DoForEach(Func<T, bool> actionAndCancelFunction, ref Option<T> lowerLimit, ref Option<T> upperLimit, bool upperLimitInclusive, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (!lowerLimit.HasValue || IsKeyGreaterThanOrEqualToLimit(midValue, lowerLimit.Value, DimensionCount))
        {
            if (upperLimitInclusive)
            {
                if (!upperLimit.HasValue || IsKeyLessThanOrEqualToLimit(midValue, upperLimit.Value, DimensionCount))
                {
                    bool cancel = actionAndCancelFunction.Invoke(midValue);
                    if (cancel)
                    {
                        return true;
                    }
                }
            }
            else
            {
                if (!upperLimit.HasValue || IsKeyLessThanLimit(midValue, upperLimit.Value, DimensionCount))
                {
                    bool cancel = actionAndCancelFunction.Invoke(midValue);
                    if (cancel)
                    {
                        return true;
                    }
                }
            }
        }

        int nextDimension = (dimension + 1) % DimensionCount;

        int? upperLimitComparisonResult = upperLimit.HasValue ? upperLimit.Value.CompareDimensionTo(midValue, dimension) : null;

        if (!upperLimitComparisonResult.HasValue || upperLimitComparisonResult.Value >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                bool cancel = DoForEach(actionAndCancelFunction, ref lowerLimit, ref upperLimit, upperLimitInclusive, nextLeftIndex, nextRightIndex, nextDimension);
                if (cancel)
                {
                    return true;
                }
            }
        }

        int? lowerLimitComparisonResult = lowerLimit.HasValue ? lowerLimit.Value.CompareDimensionTo(midValue, dimension) : null;

        if (!lowerLimitComparisonResult.HasValue || lowerLimitComparisonResult <= 0
            || (dirty && upperLimitComparisonResult == 0))
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                bool cancel = DoForEach(actionAndCancelFunction, ref lowerLimit, ref upperLimit, upperLimitInclusive, nextLeftIndex, nextRightIndex, nextDimension);
                if (cancel)
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

    internal bool Contains(T value, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (IsEqualTo(value, midValue, DimensionCount))
        {
            return true;
        }

        int nextDimension = (dimension + 1) % DimensionCount;
        int comparisonResult = value.CompareDimensionTo(midValue, dimension);

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                bool found = Contains(value, nextLeftIndex, nextRightIndex, nextDimension);

                if (found)
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
                bool found = Contains(value, nextLeftIndex, nextRightIndex, nextDimension);

                if (found)
                {
                    return true;
                }
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
    /// <returns>true if an element is found otherwise false</returns>
    public bool TryGetFirst(Option<T> lowerLimit, Option<T> upperLimit, bool upperLimitInclusive, ref T value)
    {
        if (lowerLimit.HasValue && upperLimit.HasValue)
        {
            for (int dimension = 0; dimension < DimensionCount; dimension++)
            {
                int comparisonResult = lowerLimit.Value.CompareDimensionTo(upperLimit.Value, dimension);

                if (comparisonResult > 0)
                {
                    return false;
                }
            }
        }

        return TryGetFirst(ref lowerLimit, ref upperLimit, upperLimitInclusive, 0, Count - 1, 0, ref value);
    }

    internal bool TryGetFirst(ref Option<T> lowerLimit, ref Option<T> upperLimit, bool upperLimitInclusive, int leftIndex, int rightIndex, int dimension, ref T value)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        if (!lowerLimit.HasValue || IsKeyGreaterThanOrEqualToLimit(midValue, lowerLimit.Value, DimensionCount))
        {
            if (upperLimitInclusive)
            {
                if (!upperLimit.HasValue || IsKeyLessThanOrEqualToLimit(midValue, upperLimit.Value, DimensionCount))
                {
                    value = midValue;
                    return true;
                }
            }
            else
            {
                if (!upperLimit.HasValue || IsKeyLessThanLimit(midValue, upperLimit.Value, DimensionCount))
                {
                    value = midValue;
                    return true;
                }
            }
        }

        int nextDimension = (dimension + 1) % DimensionCount;

        int? upperLimitComparisonResult = upperLimit.HasValue ? upperLimit.Value.CompareDimensionTo(midValue, dimension) : null;

        if (!upperLimitComparisonResult.HasValue || upperLimitComparisonResult.Value >= 0)
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

        int? lowerLimitComparisonResult = lowerLimit.HasValue ? lowerLimit.Value.CompareDimensionTo(midValue, dimension) : null;

        if (!lowerLimitComparisonResult.HasValue || lowerLimitComparisonResult <= 0
            || (dirty && upperLimitComparisonResult == 0))
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

[DebuggerDisplay("Count: {Count}")]
public class MetricKDTree<T> : KDTree<T> where T : IMetricTreeItem<T>
{
    public MetricKDTree(int dimensionCount, IEnumerable<T> values, bool parallel = false)
        : base(dimensionCount, values, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public MetricKDTree(int dimensionCount, IEnumerable<T> values, int maxThreadCount)
        : base(dimensionCount, values, maxThreadCount)
    {
    }

    internal MetricKDTree(int dimensionCount, T[][] values, DimensionalComparer<T>[] comparers, int maxThreadCount)
        : base(dimensionCount, values, comparers, maxThreadCount)
    {
    }

    /// <summary>
    /// Gets the value with the lowest euclidean distance between it and the given <paramref name="value"/>.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="neighbor"></param>
    /// <param name="squaredDistance"></param>
    /// <returns>true if a neighbor was found otherwise false</returns>
    /// <exception cref="ArgumentNullException"></exception>
    public bool GetNearestNeighbor(T value, out T neighbor, out double squaredDistance)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Option<T> currentNeighbor = default;
        double? minSqaredDistance = default;

        GetNearestNeighbor(ref value, ref currentNeighbor, ref minSqaredDistance, 0, Count - 1, 0);

        neighbor = currentNeighbor.Value;
        squaredDistance = minSqaredDistance ?? default;
        bool result = currentNeighbor.HasValue;

        return result;
    }

    internal void GetNearestNeighbor(ref T value, ref Option<T> neighbor, ref double? minSqaredDistance, int leftIndex, int rightIndex, int dimension)
    {
        int midIndex = (rightIndex + leftIndex) / 2;

        ref T midValue = ref Values[midIndex];
        bool dirty = Dirties[midIndex];

        double squaredDistance = GetSquaredDistance(ref value, ref midValue, DimensionCount);

        if (!minSqaredDistance.HasValue || squaredDistance < minSqaredDistance.Value)
        {
            neighbor = midValue;
            minSqaredDistance = squaredDistance;
        }

        int nextDimension = (dimension + 1) % DimensionCount;
        int comparisonResult = value.CompareDimensionTo(midValue, dimension);

        bool wasRight = false;
        bool forceLeft = false;

        if (comparisonResult >= 0)
        {
            int nextLeftIndex = midIndex + 1;
            int nextRightIndex = rightIndex;

            if (nextRightIndex >= nextLeftIndex)
            {
                GetNearestNeighbor(ref value, ref neighbor, ref minSqaredDistance, nextLeftIndex, nextRightIndex, nextDimension);

                wasRight = true;
            }

            double limitSquaredDistance = midValue.GetDimension(dimension) - value.GetDimension(dimension);
            limitSquaredDistance *= limitSquaredDistance;

            if (!minSqaredDistance.HasValue || limitSquaredDistance < minSqaredDistance.Value)
            {
                forceLeft = true;
            }
        }

        if (comparisonResult < 0 || (dirty && comparisonResult == 0) || forceLeft)
        {
            int nextLeftIndex = leftIndex;
            int nextRightIndex = midIndex - 1;

            if (nextRightIndex >= nextLeftIndex)
            {
                GetNearestNeighbor(ref value, ref neighbor, ref minSqaredDistance, nextLeftIndex, nextRightIndex, nextDimension);
            }

            if (!wasRight)
            {
                double squaredDistanceToLimit = midValue.GetDimension(dimension) - value.GetDimension(dimension);
                squaredDistanceToLimit *= squaredDistanceToLimit;

                if (!minSqaredDistance.HasValue || squaredDistanceToLimit < minSqaredDistance.Value)
                {
                    nextLeftIndex = midIndex + 1;
                    nextRightIndex = rightIndex;

                    if (nextRightIndex >= nextLeftIndex)
                    {
                        GetNearestNeighbor(ref value, ref neighbor, ref minSqaredDistance, nextLeftIndex, nextRightIndex, nextDimension);
                    }
                }
            }
        }
    }

    public static double GetSquaredDistance(ref T source, ref T target, int dimensionCount)
    {
        double result = 0;
        for (int dimension = 0; dimension < dimensionCount; dimension++)
        {
            double diff = target.GetDimension(dimension) - source.GetDimension(dimension);

            result += diff * diff;
        }

        return result;
    }
}