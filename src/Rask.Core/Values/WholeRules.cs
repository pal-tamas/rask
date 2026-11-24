namespace Rask.Values;

/// <summary>What a <see cref="Whole" /> was handed.</summary>
/// <param name="Min">The least it may be, or null.</param>
/// <param name="Max">The greatest it may be, or null.</param>
internal sealed record WholeRules(long? Min, long? Max) : IValueRules;
