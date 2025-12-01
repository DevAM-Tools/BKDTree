using System;
using System.Runtime.CompilerServices;

namespace BKDTree;

/// <summary>
/// A ref struct enumerator that provides allocation-free iteration over KDTree values.
/// Cannot be used with async/await or stored in fields, but avoids heap allocations.
/// </summary>
public ref struct ValueEnumerator<T>
{
    private readonly T[] _values;
    private int _index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueEnumerator(T[] values)
    {
        _values = values;
        _index = -1;
    }

    public readonly T Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values[_index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        int nextIndex = _index + 1;
        if (nextIndex < _values.Length)
        {
            _index = nextIndex;
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        _index = -1;
    }
}

/// <summary>
/// Wrapper struct that enables foreach over KDTree with allocation-free enumeration.
/// </summary>
public readonly ref struct ValueEnumerable<T>
{
    private readonly T[] _values;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueEnumerable(T[] values)
    {
        _values = values;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueEnumerator<T> GetEnumerator() => new(_values);

    public int Count => _values.Length;
}
