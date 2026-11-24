namespace Rask.Data;

/// <summary>What a <see cref="Number" /> was handed.</summary>
/// <param name="Min">The least it may be, or null.</param>
/// <param name="Max">The greatest it may be, or null.</param>
/// <param name="Scale">The most decimal places, or null.</param>
/// <param name="Precision">The most digits in all, or null.</param>
internal sealed record NumberRules(decimal? Min, decimal? Max, int? Scale, int? Precision) : IValueRules;
