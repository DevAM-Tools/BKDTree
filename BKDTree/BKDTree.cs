using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BKDTree;

[DebuggerDisplay("Count: {Count}")]
public class BKDTree<T, TComparer>
    where T : notnull
    where TComparer : struct, IDimensionalComparer<T>
{
    public const int DefaultBlockSize = 128;
    internal readonly int BlockSize;
    internal readonly int MaxThreadCount;
    internal readonly int DimensionCount;
    internal readonly TComparer Comparer;
    internal T[] BaseBlock;
    internal int BaseBlockCount;
    internal KDTree<T, TComparer>[] Trees = new KDTree<T, TComparer>[1];

    internal readonly DimensionalComparer<T, TComparer>[] Comparers;

    public long Count { get; private set; }

    public BKDTree(int dimensionCount, TComparer comparer, int blockSize = DefaultBlockSize, bool parallel = false)
        : this(dimensionCount, comparer, blockSize, parallel ? Environment.ProcessorCount : 1)
    {
    }

    public BKDTree(int dimensionCount, TComparer comparer, int blockSize, int maxThreadCount)
    {
        if (dimensionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensionCount));
        }

        Comparer = comparer;
        DimensionCount = dimensionCount;
        MaxThreadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, maxThreadCount));

        if (blockSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(blockSize));
        }

        BlockSize = blockSize;
        BaseBlock = new T[BlockSize];

        Comparers = new DimensionalComparer<T, TComparer>[DimensionCount];
        for (int dimension = 0; dimension < DimensionCount; dimension++)
        {
            Comparers[dimension] = new(dimension, Comparer);
        }
    }

    internal virtual KDTree<T, TComparer> CreateNewTree(IList<Segment<T>> values)
    {
        KDTree<T, TComparer> result = new(DimensionCount, values, Comparer, Comparers, MaxThreadCount);
        return result;
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

        if (BaseBlockCount >= BlockSize)
        {
            int emptyIndex = 0;
            for (emptyIndex = 0; emptyIndex < Trees.Length; emptyIndex++)
            {
                KDTree<T, TComparer> tree = Trees[emptyIndex];
                if (tree is null)
                {
                    break;
                }
            }

            if (emptyIndex >= 32)
            {
                throw new InvalidOperationException("Insertion failed. Tree is full.");
            }

            Segment<T>[] values = new Segment<T>[emptyIndex + 1];
            values[0] = new(BaseBlock);
            for (int i = 0; i < emptyIndex; i++)
            {
                values[i + 1] = new(Trees[i].Values);
            }

            KDTree<T, TComparer> newTree = CreateNewTree(values);

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
    /// Inserts a list of values without any checks for duplicates.
    /// </summary>
    /// <param name="values">The values that shall be inserted must not be null.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public void Insert(List<T> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (values.Count == 0)
        {
            return;
        }

        if (values.Count <= 2 * BlockSize)
        {
            for (int i = 0; i < values.Count; i++)
            {
                Insert(values[i]);
            }

            return;
        }

        Stack<Segment<T>> segments = SegmentStackPool<T>.Rent(Trees.Length + 2);
        segments.Push(new(values));
        if (BaseBlockCount > 0)
        {
            segments.Push(new(BaseBlock, 0, BaseBlockCount));
        }

        long newCount = BaseBlockCount + values.Count;
        int cumulativeBlockSize = BlockSize; // BaseBlock
        for (int i = 0; i < 32; i++)
        {
            if (newCount < cumulativeBlockSize)
            {
                break;
            }

            int currentBlockSize = BlockSize << i;
            if (i < Trees.Length)
            {
                KDTree<T, TComparer> tree = Trees[i];

                if (tree is not null)
                {
                    newCount += currentBlockSize;
                    Segment<T> segment = new(tree.Values);
                    segments.Push(segment);
                }
            }

            cumulativeBlockSize += currentBlockSize;
        }

        // A bitmask that represents the used blocks in the new tree
        // If the bit is not set the block will stay null
        long usedBlocksBitmask = newCount / BlockSize;
        // Handle edge case: if usedBlocksBitmask is 0, Math.Log returns -Infinity
        int newTreeCount = usedBlocksBitmask <= 0 ? 0 : 1 + (int)Math.Log(usedBlocksBitmask, 2);
        KDTree<T, TComparer>[] newTrees = new KDTree<T, TComparer>[newTreeCount];

        if (newTreeCount > Trees.Length)
        {
            Array.Resize(ref Trees, newTreeCount);
        }

        // Will be reused for each tree - rent from pool to avoid allocation
        List<Segment<T>> currentSegments = SegmentListPool<T>.Rent(segments.Count);

        int currentBit = 1;
        int segmentOffset = 0;
        for (int i = 0; i < newTrees.Length; i++)
        {
            int currentCount = 0;
            currentSegments.Clear();

            int currentBlockSize = BlockSize << i;

            bool isBlockUsed = (currentBit & usedBlocksBitmask) != 0;
            if (isBlockUsed)
            {
                while (segments.Count > 0)
                {
                    Segment<T> segment = segments.Peek();
                    int remainingSegmentLength = segment.Length - segmentOffset;

                    // Does the segment fit into as a whole? 
                    if (currentCount + remainingSegmentLength < currentBlockSize)
                    {
                        Segment<T> currentSegment = new(segment.Values, segmentOffset, remainingSegmentLength);
                        currentSegments.Add(currentSegment);

                        segmentOffset += remainingSegmentLength;
                        currentCount += remainingSegmentLength;
                    }
                    else // Segment needs to be sliced
                    {
                        int missingCount = currentBlockSize - currentCount;
                        Segment<T> currentSegment = new(segment.Values, segmentOffset, missingCount);
                        currentSegments.Add(currentSegment);

                        segmentOffset += missingCount;
                        currentCount += missingCount;
                    }

                    if (segmentOffset >= segment.Length)
                    {
                        segmentOffset = 0;
                        segments.Pop();
                    }

                    if (currentCount == currentBlockSize)
                    {
                        break;
                    }
                }

                KDTree<T, TComparer> tree = CreateNewTree(currentSegments);
                newTrees[i] = tree;
            }

            currentBit <<= 1;
        }

        T[] newBaseBlock = new T[BlockSize];
        int currentBaseBlockIndex = 0;
        while (segments.Count > 0)
        {
            Segment<T> segment = segments.Pop();
            int remainingSegmentLength = segment.Length - segmentOffset;

            if (segment.Values is T[] array)
            {
                Array.Copy(array, segment.Offset + segmentOffset, newBaseBlock, currentBaseBlockIndex, remainingSegmentLength);
            }
            else if (segment.Values is List<T> list)
            {
                list.CopyTo(segment.Offset + segmentOffset, newBaseBlock, currentBaseBlockIndex, remainingSegmentLength);
            }
            currentBaseBlockIndex += remainingSegmentLength;
            segmentOffset = 0; // Reset for next segment
        }

        BaseBlock = newBaseBlock;
        BaseBlockCount = currentBaseBlockIndex;
        for (int i = 0; i < newTrees.Length; i++)
        {
            Trees[i] = newTrees[i];
        }
        Count += values.Count;

        // Return pooled objects
        SegmentListPool<T>.Return(currentSegments);
        SegmentStackPool<T>.Return(segments);
    }

    /// <summary>
    /// Gets all values. Consider using <see cref="DoForEach(Action{T})"/> ot <see cref="DoForEach(Func{T, bool})"/> if performance is critical.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<T> GetAll()
    {
        for (int i = 0; i < BaseBlockCount; i++)
        {
            T currentValue = BaseBlock[i];
            yield return currentValue;
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T, TComparer> tree = Trees[i];
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
        T[] baseBlock = BaseBlock;
        int baseBlockCount = BaseBlockCount;
        for (int i = 0; i < baseBlockCount; i++)
        {
            if (func.Invoke(baseBlock[i]))
            {
                return true;
            }
        }

        KDTree<T, TComparer>[] trees = Trees;
        for (int i = 0; i < trees.Length; i++)
        {
            KDTree<T, TComparer> tree = trees[i];
            if (tree is null)
            {
                continue;
            }

            if (tree.DoForEach(ref func))
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
        T[] baseBlock = BaseBlock;
        int baseBlockCount = BaseBlockCount;
        for (int i = 0; i < baseBlockCount; i++)
        {
            if (func.Invoke(in baseBlock[i]))
            {
                return true;
            }
        }

        KDTree<T, TComparer>[] trees = Trees;
        for (int i = 0; i < trees.Length; i++)
        {
            KDTree<T, TComparer> tree = trees[i];
            if (tree is null)
            {
                continue;
            }

            if (tree.DoForEachRef(ref func))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all matching values. Since <see cref="BKDTree{T, TComparer}"/> does allow duplicates this may be more than one. Consider using <see cref="DoForEach(T, Func{T, bool})"/> if performance is critical.
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            T currentValue = BaseBlock[i];
            if (IsEqualTo(currentValue, value))
            {
                yield return currentValue;
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T, TComparer> tree = Trees[i];
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

    /// <summary>
    /// Applies an <paramref name="actionAndCancelFunction"/> to every matching values. Since <see cref="BKDTree{T, TComparer}"/> does allow duplicates this may be more than one. Prefer this over <see cref="Get(T)"/> in performance critical paths.
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DoForEach<TFunc>(T value, ref TFunc func)
        where TFunc : struct, IForEachFunc<T>
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        for (int i = 0; i < BaseBlockCount; i++)
        {
            T currentValue = BaseBlock[i];
            if (IsEqualTo(currentValue, value))
            {
                if (func.Invoke(currentValue))
                {
                    return true;
                }
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T, TComparer> tree = Trees[i];
            if (tree is null)
            {
                continue;
            }

            if (tree.DoForEach(value, ref func))
            {
                return true;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool DoForEach<TFunc>(ref TFunc func, Option<T> lowerLimit, Option<T> upperLimit, bool upperLimitInclusive)
        where TFunc : struct, IForEachFunc<T>
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            T currentValue = BaseBlock[i];
            if (!lowerLimit.HasValue || IsKeyGreaterThanOrEqualToLimit(in currentValue, lowerLimit.Value))
            {
                if (upperLimitInclusive)
                {
                    if (!upperLimit.HasValue || IsKeyLessThanOrEqualToLimit(in currentValue, upperLimit.Value))
                    {
                        if (func.Invoke(currentValue))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    if (!upperLimit.HasValue || IsKeyLessThanLimit(in currentValue, upperLimit.Value))
                    {
                        if (func.Invoke(currentValue))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T, TComparer> tree = Trees[i];
            if (tree is null)
            {
                continue;
            }

            if (tree.DoForEach(ref func, lowerLimit, upperLimit, upperLimitInclusive))
            {
                return true;
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            ref T currentValue = ref BaseBlock[i];
            if (IsEqualTo(currentValue, value))
            {
                return true;
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T, TComparer> tree = Trees[i];
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
    /// <param name="value">The value that will be set if an element is found</param>
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

        for (int i = 0; i < BaseBlockCount; i++)
        {
            T currentValue = BaseBlock[i];
            if (!lowerLimit.HasValue || IsKeyGreaterThanOrEqualToLimit(in currentValue, lowerLimit.Value))
            {
                if (upperLimitInclusive)
                {
                    if (!upperLimit.HasValue || IsKeyLessThanOrEqualToLimit(in currentValue, upperLimit.Value))
                    {
                        value = currentValue;
                        return true;
                    }
                }
                else
                {
                    if (!upperLimit.HasValue || IsKeyLessThanLimit(in currentValue, upperLimit.Value))
                    {
                        value = currentValue;
                        return true;
                    }
                }
            }
        }

        for (int i = 0; i < Trees.Length; i++)
        {
            KDTree<T, TComparer> tree = Trees[i];
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
}