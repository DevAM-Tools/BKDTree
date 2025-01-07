using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class BKDTree<T> where T : ITreeItem<T>
{
    public const int DefaultBlockSize = 128;
    internal readonly int BlockSize;
    internal readonly int DimensionCount;
    internal T[] BaseBlock;
    internal int BaseBlockCount;
    internal KDTree<T>[] Trees = new KDTree<T>[1];
    private long _EnumerationCount;

    internal readonly DimensionalComparer<T>[] Comparers;

    public bool IsMetric { get; } = typeof(IMetricTreeItem<T>).IsAssignableFrom(typeof(T));

    public long Count { get; private set; }

    public BKDTree(int dimensionCount, int blockSize = DefaultBlockSize)
    {
        if (dimensionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionCount));
        }

        DimensionCount = (byte)dimensionCount;

        if (blockSize < 2)
        {
            throw new ArgumentNullException(nameof(blockSize));
        }

        BlockSize = blockSize;
        BaseBlock = new T[BlockSize];

        Comparers = new DimensionalComparer<T>[blockSize];
        for (int dimension = 0; dimension < DimensionCount; dimension++)
        {
            Comparers[dimension] = new(dimension);
        }
    }

    protected virtual KDTree<T> CreateNewTree(T[][] values)
    {
        KDTree<T> result = new(DimensionCount, values, Comparers);
        return result;
    }

    /// <summary>
    /// Inserts a value without any checks for duplicates.
    /// </summary>
    /// <param name="value">The value that shall be inserted must not be null.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public void Insert(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (Interlocked.Read(ref _EnumerationCount) > 0L)
        {
            throw new InvalidOperationException("Modifications during enumerations are not allowed.");
        }

        if (BaseBlockCount >= BlockSize)
        {
            int emptyIndex = 0;
            for (emptyIndex = 0; emptyIndex < Trees.Length; emptyIndex++)
            {
                ref KDTree<T> tree = ref Trees[emptyIndex];
                if (tree is null)
                {
                    break;
                }
            }

            if (emptyIndex >= 32)
            {
                throw new InvalidOperationException("Insertion failed. Tree is full.");
            }

            T[][] values = new T[emptyIndex + 1][];
            values[0] = BaseBlock;
            for (int i = 0; i < emptyIndex; i++)
            {
                values[i + 1] = Trees[i].Values;
            }

            KDTree<T> newTree = CreateNewTree(values);

            if (emptyIndex >= Trees.Length)
            {
                Array.Resize(ref Trees, Trees.Length + 1);
            }

            Trees[emptyIndex] = newTree;

            BaseBlock = new T[BlockSize];
            BaseBlockCount = 0;

            for (int i = 0; i < emptyIndex; i++)
            {
                Trees[i] = null;
            }
        }

        BaseBlock[BaseBlockCount] = value;
        BaseBlockCount++;
        Count++;
    }

    /// <summary>
    /// Gets all values. Consider using <see cref="DoForEach(Action{T})"/> ot <see cref="DoForEach(Func{T, bool})"/> if performance is critical.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<T> GetAll()
    {
        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                yield return currentValue;
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                foreach (T currentValue in tree.GetAll())
                {
                    yield return currentValue;
                }
            }
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
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

        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                action.Invoke(currentValue);
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                tree.DoForEach(action);
            }
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
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

        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                bool cancel = actionAndCancelFunction.Invoke(currentValue);
                if (cancel)
                {
                    return true;
                }
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                bool cancel = tree.DoForEach(actionAndCancelFunction);
                if (cancel)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
        }
    }

    /// <summary>
    /// Gets all matching values. Since <see cref="BKDTree{T}"/> does allow duplicates this may be more than one. Consider using <see cref="DoForEach(T, Func{T, bool}"/> if performance is critical.
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

        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                if (KDTree<T>.IsEqualTo(currentValue, value, DimensionCount))
                {
                    yield return currentValue;
                }
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                foreach (T currentValue in tree.Get(value))
                {
                    yield return currentValue;
                }
            }
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
        }
    }

    /// <summary>
    /// Applies an <paramref name="actionAndCancelFunction"/> to every mathing values. Since <see cref="BKDTree{T}"/> does allow duplicates this may be more than one. Prefer this over <see cref="Get(T)"/> in performance critical paths.
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

        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                if (KDTree<T>.IsEqualTo(currentValue, value, DimensionCount))
                {
                    bool cancel = actionAndCancelFunction.Invoke(currentValue);
                    if (cancel)
                    {
                        return true;
                    }
                }
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                bool cancel = tree.DoForEach(value, actionAndCancelFunction);
                if (cancel)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
        }
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

        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

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

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                if (!lowerLimit.HasValue || KDTree<T>.IsKeyGreaterThanOrEqualToLimit(currentValue, lowerLimit.Value, DimensionCount))
                {
                    if (upperLimitInclusive)
                    {
                        if (!upperLimit.HasValue || KDTree<T>.IsKeyLessThanOrEqualToLimit(currentValue, upperLimit.Value, DimensionCount))
                        {
                            bool cancel = actionAndCancelFunction.Invoke(currentValue);
                            if (cancel)
                            {
                                return true;
                            }
                        }
                    }
                    else
                    {
                        if (!upperLimit.HasValue || KDTree<T>.IsKeyLessThanLimit(currentValue, upperLimit.Value, DimensionCount))
                        {
                            bool cancel = actionAndCancelFunction.Invoke(currentValue);
                            if (cancel)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                bool cancel = tree.DoForEach(actionAndCancelFunction, lowerLimit, upperLimit, upperLimitInclusive);
                if (cancel)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
        }
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            ref T currentValue = ref BaseBlock[i];
            if (KDTree<T>.IsEqualTo(currentValue, value, DimensionCount))
            {
                return true;
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T> tree = Trees[i];
            if (tree is null)
            {
                continue;
            }

            bool contains = tree.Contains(value);
            if (contains)
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
    /// <returns>true if an element is found otherwise false</returns>
    public bool TryGetFirst(Option<T> lowerLimit, Option<T> upperLimit, bool upperLimitInclusive, ref T value)
    {
        try
        {
            Interlocked.Add(ref _EnumerationCount, 1);

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

            for (int i = 0; i < BaseBlockCount; i++)
            {
                T currentValue = BaseBlock[i];
                if (!lowerLimit.HasValue || KDTree<T>.IsKeyGreaterThanOrEqualToLimit(currentValue, lowerLimit.Value, DimensionCount))
                {
                    if (upperLimitInclusive)
                    {
                        if (!upperLimit.HasValue || KDTree<T>.IsKeyLessThanOrEqualToLimit(currentValue, upperLimit.Value, DimensionCount))
                        {
                            value = currentValue;
                            return true;
                        }
                    }
                    else
                    {
                        if (!upperLimit.HasValue || KDTree<T>.IsKeyLessThanLimit(currentValue, upperLimit.Value, DimensionCount))
                        {
                            value = currentValue;
                            return true;
                        }
                    }
                }
            }

            for (int i = 0; i < Trees.Length; i++)
            {
                KDTree<T> tree = Trees[i];
                if (tree is null)
                {
                    continue;
                }

                bool found = tree.TryGetFirst(lowerLimit, upperLimit, upperLimitInclusive, ref value);
                if (found)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Interlocked.Add(ref _EnumerationCount, -1);
        }
    }
}

[DebuggerDisplay("Count: {Count}")]
public class MetricBKDTree<T> : BKDTree<T> where T : IMetricTreeItem<T>
{
    public MetricBKDTree(int dimensionCount, int blockSize = DefaultBlockSize) : base(dimensionCount, blockSize)
    {
    }

    protected override KDTree<T> CreateNewTree(T[][] values)
    {
        KDTree<T> result = new MetricKDTree<T>(DimensionCount, values, Comparers);
        return result;
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            ref T currentValue = ref BaseBlock[i];

            double distance = MetricKDTree<T>.GetSquaredDistance(ref value, ref currentValue, DimensionCount);

            if (!minSqaredDistance.HasValue || distance < minSqaredDistance.Value)
            {
                currentNeighbor = currentValue;
                minSqaredDistance = distance;
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            MetricKDTree<T> tree = Trees[i] as MetricKDTree<T>;
            if (tree is null)
            {
                continue;
            }

            tree.GetNearestNeighbor(ref value, ref currentNeighbor, ref minSqaredDistance, 0, tree.Count - 1, 0);
        }

        neighbor = currentNeighbor.Value;
        squaredDistance = minSqaredDistance.HasValue ? minSqaredDistance.Value : default;
        bool result = currentNeighbor.HasValue;

        return result;
    }
}