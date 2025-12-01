#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace BKDTree;

/// <summary>
/// Thread-safe object pool for List&lt;Segment&lt;T&gt;&gt; instances to reduce allocations during bulk inserts.
/// Uses a simple lock-free stack implementation.
/// </summary>
internal static class SegmentListPool<T>
{
    private const int MaxPoolSize = 16;
    
    // Simple array-based pool with atomic index
    private static readonly List<Segment<T>>?[] _pool = new List<Segment<T>>?[MaxPoolSize];
    private static int _poolCount;

    /// <summary>
    /// Rents a List from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <param name="capacity">Minimum capacity for the list.</param>
    /// <returns>A List instance that should be returned via Return().</returns>
    public static List<Segment<T>> Rent(int capacity)
    {
        // Try to get from pool
        int count = Volatile.Read(ref _poolCount);
        while (count > 0)
        {
            int newCount = count - 1;
            if (Interlocked.CompareExchange(ref _poolCount, newCount, count) == count)
            {
                List<Segment<T>>? list = Interlocked.Exchange(ref _pool[newCount], null);
                if (list is not null)
                {
                    list.Clear();
                    if (list.Capacity < capacity)
                    {
                        list.Capacity = capacity;
                    }
                    return list;
                }
                // List was null due to race condition - continue trying
                count = Volatile.Read(ref _poolCount);
                continue;
            }
            count = Volatile.Read(ref _poolCount);
        }

        // Pool was empty, create new
        return new List<Segment<T>>(capacity);
    }

    /// <summary>
    /// Returns a List to the pool for reuse.
    /// </summary>
    /// <param name="list">The list to return. Will be cleared automatically on next Rent.</param>
    public static void Return(List<Segment<T>> list)
    {
        if (list is null) return;

        // Don't pool very large lists to avoid memory bloat
        if (list.Capacity > 1024)
        {
            return;
        }

        int count = Volatile.Read(ref _poolCount);
        while (count < MaxPoolSize)
        {
            if (Interlocked.CompareExchange(ref _poolCount, count + 1, count) == count)
            {
                _pool[count] = list;
                return;
            }
            count = Volatile.Read(ref _poolCount);
        }
        // Pool is full, let GC handle it
    }
}

/// <summary>
/// Thread-safe object pool for Stack&lt;Segment&lt;T&gt;&gt; instances.
/// </summary>
internal static class SegmentStackPool<T>
{
    private const int MaxPoolSize = 16;
    
    private static readonly Stack<Segment<T>>?[] _pool = new Stack<Segment<T>>?[MaxPoolSize];
    private static int _poolCount;

    public static Stack<Segment<T>> Rent(int capacity)
    {
        int count = Volatile.Read(ref _poolCount);
        while (count > 0)
        {
            int newCount = count - 1;
            if (Interlocked.CompareExchange(ref _poolCount, newCount, count) == count)
            {
                Stack<Segment<T>>? stack = Interlocked.Exchange(ref _pool[newCount], null);
                if (stack is not null)
                {
                    stack.Clear();
                    return stack;
                }
                // Stack was null due to race condition - continue trying
                count = Volatile.Read(ref _poolCount);
                continue;
            }
            count = Volatile.Read(ref _poolCount);
        }

        return new Stack<Segment<T>>(capacity);
    }

    public static void Return(Stack<Segment<T>> stack)
    {
        if (stack is null) return;

        // Don't pool if it grew too large
        if (stack.Count > 64)
        {
            return;
        }

        int count = Volatile.Read(ref _poolCount);
        while (count < MaxPoolSize)
        {
            if (Interlocked.CompareExchange(ref _poolCount, count + 1, count) == count)
            {
                _pool[count] = stack;
                return;
            }
            count = Volatile.Read(ref _poolCount);
        }
    }
}
