namespace Rask.Values;

/// <summary>What a <see cref="Moment" /> was handed.</summary>
/// <param name="NotBefore">The earliest moment it may be, or null.</param>
/// <param name="NotAfter">The latest moment it may be, or null.</param>
/// <param name="NotAfterNow">Whether it may not be after <see cref="Clock.Wall" />.</param>
internal sealed record MomentRules(DateTime? NotBefore, DateTime? NotAfter, bool NotAfterNow) : IValueRules;
