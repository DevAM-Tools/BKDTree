using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BKDTree;

[DebuggerDisplay("Count: {Values is not null ? Length : 0}")]
internal readonly struct Segment<T>
{
    internal readonly IList<T> Values;
    internal readonly int Offset;
    internal readonly int Length;

    internal Segment(IList<T> values, int offset, int length)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if ((offset + length) > values.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Values = values;
        Offset = offset;
        Length = length;
    }

    public Segment(IList<T> values)
        : this(values, 0, values?.Count ?? 0)
    { }
}