using System.Runtime.CompilerServices;

namespace Rask.Core.Forms;

// A rule that is asked only about a value somebody filled in: what a field's type-carried rule becomes under an
// object that may be left wholly empty (RaskValidation.RegisterOptionalFields). Unfilled is no value, blank text
// or a box nobody ticked. One wrapper per rule, kept for as long as the rule is: a bound field asks on every
// render, and the rules handed over are themselves kept.
internal static class WhenFilled
{
    private static readonly ConditionalWeakTable<Delegate, Delegate> Kept = [];

    public static Delegate Of<T>(Delegate rule) => Kept.GetValue(rule, static rule => Wrap<T>(rule));

    private static Delegate Wrap<T>(Delegate rule) => rule switch
    {
        Validate<T> now => new Validate<T>(value => IsFilled(value) ? now(value) : []),
        Func<T, ValueTask<IEnumerable<string>>> awaited => new Func<T, ValueTask<IEnumerable<string>>>(
            value => IsFilled(value) ? awaited(value) : new ValueTask<IEnumerable<string>>([])),
        _ => throw Callback.Unexpected(rule),
    };

    private static bool IsFilled<T>(T value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        bool ticked => ticked,
        _ => true,
    };
}
