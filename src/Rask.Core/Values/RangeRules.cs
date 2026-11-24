namespace Rask.Values;

/// <summary>What a <see cref="Range" /> was handed.</summary>
/// <param name="Strict">Whether the end must be after the start, not only not before it.</param>
/// <param name="Min">The least either end may be, of the ends' type, or null.</param>
/// <param name="Max">The greatest either end may be, of the ends' type, or null.</param>
internal sealed record RangeRules(bool Strict, IComparable? Min, IComparable? Max) : IValueRules;
