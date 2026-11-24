namespace Rask.Data;

/// <summary>What a <see cref="Day" /> was handed.</summary>
/// <param name="NotBefore">The earliest day it may be, or null.</param>
/// <param name="NotAfter">The latest day it may be, or null.</param>
/// <param name="NotAfterToday">Whether it may not be after <see cref="Clock.Today" />.</param>
internal sealed record DayRules(DateOnly? NotBefore, DateOnly? NotAfter, bool NotAfterToday) : IValueRules;
