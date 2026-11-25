using System.Collections.Concurrent;
using Rask.Values;

namespace Rask;

/// <summary>
///     A value object of one day or one moment made by a control, through the generated <c>TryCreate</c> its assembly
///     registers (<see cref="ValueFields" />) — the one way in that asks every rule — and the rules its base was
///     handed, read without checking any.
/// </summary>
internal static class UiMadeValue
{
    // A type declares its rules, so they are read once; "today" in them is the caller's, as it is drawn.
    private static readonly ConcurrentDictionary<Type, IValueRules?> Declared = new();

    /// <summary>The value object holding <paramref name="held" />, or what its rules say against it.</summary>
    /// <param name="held">The one value it holds.</param>
    /// <typeparam name="TValue">The value object: a <see cref="Day" />, a <see cref="Moment" />.</typeparam>
    internal static (TValue? Made, string? Refused) Make<TValue>(object held)
        where TValue : Value =>
        Field<TValue>().Make(held, out var problems) is TValue made
            ? (made, null)
            : (null, string.Join(" ", GeneratedValueRules.Messages(problems)));

    /// <summary>The rules its base was handed: its bounds.</summary>
    /// <typeparam name="TValue">The value object.</typeparam>
    internal static IValueRules? Rules<TValue>()
        where TValue : Value =>
        Declared.GetOrAdd(typeof(TValue), static (_, field) => Read(field), Field<TValue>());

    // Anything of the type it holds will do to read them by: nothing is checked.
    private static IValueRules? Read(ValueField field) =>
        DeclaredRules.Read(() => field.Make(field.Held == typeof(DateTime) ? default(DateTime) : default(DateOnly), out _));

    private static ValueField Field<TValue>()
        where TValue : Value =>
        ValueFields.Of(typeof(TValue)) ?? throw new InvalidOperationException(
            $"Ui.DatePicker.Bind(() => …) over a {typeof(TValue).Name} makes it with the TryCreate the Rask generator writes: "
            + $"declare {typeof(TValue).Name} as a public `partial record` of one value in a project the generator runs on.");
}
