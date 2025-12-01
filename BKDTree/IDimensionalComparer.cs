using System;

namespace BKDTree;

/// <summary>
/// Interface for comparing values along a specific dimension.
/// Implement this as a struct for best performance (enables JIT inlining).
/// </summary>
/// <typeparam name="T">The type of values to compare</typeparam>
public interface IDimensionalComparer<T>
{
    /// <summary>
    /// Compares two values along the specified dimension.
    /// </summary>
    /// <param name="left">The first value to compare (passed by readonly reference for large structs)</param>
    /// <param name="right">The second value to compare (passed by readonly reference for large structs)</param>
    /// <param name="dimension">The dimension index to compare along</param>
    /// <returns>
    /// A negative value if left is less than right along the dimension,
    /// zero if they are equal,
    /// a positive value if left is greater than right.
    /// </returns>
    int Compare(in T left, in T right, int dimension);
}

/// <summary>
/// Interface for retrieving dimension values as doubles.
/// Required for metric operations like nearest neighbor search.
/// Implement this as a struct for best performance (enables JIT inlining).
/// </summary>
/// <typeparam name="T">The type of values</typeparam>
public interface IDimensionalMetric<T>
{
    /// <summary>
    /// Gets the value of the specified dimension as a double.
    /// </summary>
    /// <param name="value">The value to extract the dimension from (passed by readonly reference for large structs)</param>
    /// <param name="dimension">The dimension index</param>
    /// <returns>The dimension value as a double</returns>
    double GetDimension(in T value, int dimension);
}

/// <summary>
/// Interface for a struct-based callback function with cancellation support.
/// Implement this as a struct for zero-allocation iteration with full inlining.
/// </summary>
/// <typeparam name="T">The type of values to process</typeparam>
public interface IForEachFunc<T>
{
    /// <summary>
    /// Invoked for each value.
    /// </summary>
    /// <param name="value">The current value</param>
    /// <returns>true to cancel iteration, false to continue</returns>
    bool Invoke(T value);
}

/// <summary>
/// Interface for a struct-based callback function with ref readonly parameter and cancellation support.
/// Use this for large structs to avoid copying. Implement as a struct for zero-allocation iteration.
/// </summary>
/// <typeparam name="T">The type of values to process</typeparam>
public interface IForEachRefFunc<T>
{
    /// <summary>
    /// Invoked for each value with a readonly reference.
    /// </summary>
    /// <param name="value">A readonly reference to the current value</param>
    /// <returns>true to cancel iteration, false to continue</returns>
    bool Invoke(in T value);
}

/// <summary>
/// Internal wrapper struct that adapts an Action delegate to IForEachFunc.
/// </summary>
internal readonly struct ActionWrapper<T> : IForEachFunc<T>
{
    private readonly Action<T> _action;

    public ActionWrapper(Action<T> action) => _action = action;

    public bool Invoke(T value)
    {
        _action(value);
        return false;
    }
}

/// <summary>
/// Internal wrapper struct that adapts a Func delegate to IForEachFunc.
/// </summary>
internal readonly struct FuncWrapper<T> : IForEachFunc<T>
{
    private readonly Func<T, bool> _func;

    public FuncWrapper(Func<T, bool> func) => _func = func;

    public bool Invoke(T value) => _func(value);
}
