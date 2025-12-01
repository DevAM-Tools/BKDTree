using System.Runtime.CompilerServices;

namespace BKDTree;

public readonly struct Option<TValue>
{
    public readonly bool HasValue;
    public readonly TValue Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Option(bool hasValue, TValue value)
    {
        HasValue = hasValue;
        Value = value;
    }

    /// <summary>
    /// Returns the value if present, otherwise returns the default value for the type.
    /// Avoids branching in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue GetValueOrDefault() => Value;

    /// <summary>
    /// Returns the value if present, otherwise returns the specified default value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue GetValueOrDefault(TValue defaultValue) => HasValue ? Value : defaultValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Option<TValue>(TValue value) => new(true, value);

    public readonly override string ToString()
    {
        return HasValue ? $"{Value}" : "";
    }
}