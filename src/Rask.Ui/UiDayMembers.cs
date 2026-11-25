using System.Linq.Expressions;
using Rask.Values;

namespace Rask;

/// <summary>
///     The members a day picker binds beside a <c>DateOnly</c>, each read as its day and written in its own type
///     (<see cref="UiDayMember{TMember}" />): a moment keeps its time of day — seconds and its <c>Kind</c> too — on a
///     new day, and its first day is midnight, as <see cref="UiDayAndTime{T}" /> keeps them.
/// </summary>
internal static class UiDayMembers
{
    /// <summary>A <c>DateTime</c>: it always holds a moment, so emptied it says it must be given.</summary>
    internal static UiDayMember<DateTime> Moment(Expression<Func<DateTime>> bind) =>
        new(bind, static held => UiDayAndTimeValue.Day(UiDayAndTimeValue.Read(held)), static (held, day) => (OnDay(held, day), null), holdsNone: false);

    /// <summary>A <c>DateTime?</c>: emptied, it is null.</summary>
    internal static UiDayMember<DateTime?> Moment(Expression<Func<DateTime?>> bind) =>
        new(bind, static held => UiDayAndTimeValue.Day(UiDayAndTimeValue.Read(held)), static (held, day) => (OnDay(held, day), null), holdsNone: true);

    /// <summary>A <see cref="Values.Moment" /> value object, made on the day picked at the time it held; emptied, it is null.</summary>
    internal static UiDayMember<TMoment> Moment<TMoment>(Expression<Func<TMoment?>> bind)
        where TMoment : Values.Moment =>
        new(bind, static held => held?.Day, static (held, day) => UiMadeValue.Make<TMoment>(OnDay(held?.Value, day)), holdsNone: true,
            UiMadeValue.Rules<TMoment>() is MomentRules rules
                ? (Day(rules.NotBefore), Latest(Day(rules.NotAfter), rules.NotAfterNow))
                : default);

    /// <summary>A <see cref="Values.Day" /> value object, made on the day picked; emptied, it is null.</summary>
    internal static UiDayMember<TDay> Day<TDay>(Expression<Func<TDay?>> bind)
        where TDay : Values.Day =>
        new(bind, static held => held?.Value, static (_, day) => UiMadeValue.Make<TDay>(day), holdsNone: true,
            UiMadeValue.Rules<TDay>() is DayRules rules
                ? (rules.NotBefore, Latest(rules.NotAfter, rules.NotAfterToday))
                : default);

    private static DateTime OnDay(DateTime? held, DateOnly day) => UiDayAndTimeValue.OnDay(UiDayAndTimeValue.Read(held), day)!.Value;

    private static DateOnly? Day(DateTime? moment) => UiDayAndTimeValue.Day(moment);

    // The latest day declared, or today where that is sooner and the rules say no later than today (or now).
    private static DateOnly? Latest(DateOnly? declared, bool notAfterToday) =>
        notAfterToday && (declared is not { } latest || Clock.Today < latest) ? Clock.Today : declared;
}
