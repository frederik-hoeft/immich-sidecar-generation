using System.Diagnostics.CodeAnalysis;

namespace MkSidecar.Utils;

/// <summary>
/// Represents a contiguous segment of a string, defined by a reference to the original string and an offset and length.
/// It is designed to avoid unnecessary allocations when slicing strings, similar to <see cref="ReadOnlySpan{T}"/>,
/// but it can be used in contexts where spans are not allowed (e.g. async methods, fields, etc.).
/// </summary>
internal readonly struct StringSegment
{
    private readonly string? _value;
    private readonly int _start;
    public readonly int Length;

    public StringSegment(string? value) : this()
    {
        _value = value;
        if (value is not null)
        {
            Length = value.Length;
        }
    }

    public StringSegment(string? value, int start, int length)
    {
        if (value is null)
        {
            // start and length must be zero
            if (start != 0 || length != 0)
            {
                ThrowArgumentOutOfRange();
            }
            this = default;
            return;
        }
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start + length > value.Length)
        {
            ThrowArgumentOutOfRange();
        }
        _value = value;
        _start = start;
        Length = length;
    }

    public bool IsEmpty => Length == 0;

    public StringSegment Slice(int start) => Slice(start, Length - start);

    public StringSegment Slice(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, Length);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, Length);
        return new StringSegment(_value, _start + start, length);
    }

    public char this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
            return _value![_start + index];
        }
    }

    public StringSegment this[Range range]
    {
        get
        {
            (int start, int length) = range.GetOffsetAndLength(Length);
            return Slice(start, length);
        }
    }

    public ReadOnlySpan<char> AsSpan() => _value.AsSpan(_start, Length);

    public override string ToString() => _value?.Substring(_start, Length) ?? string.Empty;

    [DoesNotReturn]
    private static void ThrowArgumentOutOfRange() => throw new ArgumentOutOfRangeException("Either start or length is out of range.");

    public static implicit operator ReadOnlySpan<char>(StringSegment s) => s.AsSpan();

    public static implicit operator StringSegment(string? s) => new(s);
}
