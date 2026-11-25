namespace Rask.Values;

/// <summary>What an <see cref="Integer" /> was handed.</summary>
/// <param name="Min">The least it may be, or null.</param>
/// <param name="Max">The greatest it may be, or null.</param>
internal sealed record IntegerRules(long? Min, long? Max) : IValueRules;
