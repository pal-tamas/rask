using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     What a kit control says when it refuses what it was sent: once, at Warning on <c>Rask.Forms</c>, naming the
///     control and never the value (<see cref="OfferedOptions.Refused" />) — the rule #1270 and #1271 set for a
///     choice, here for the controls that write through handlers of their own (#1272).
/// </summary>
internal static class UiRefusals
{
    /// <summary>
    ///     Whether <paramref name="control" /> is drawn disabled, and so refuses whatever it was sent — said once.
    ///     Every write of <see cref="UiFormCommit" /> asks first; a handler that keeps state of its own before it
    ///     writes asks before that.
    /// </summary>
    /// <param name="control">The control one of whose handlers was sent a value.</param>
    /// <param name="accessor">What it is bound to, which names it in the log; null while controlled.</param>
    internal static bool Locked(object control, ExpressionAccessor.Accessor? accessor)
    {
        if (control is not IUiLockable { Locked: true } locked)
        {
            return false;
        }

        OfferedOptions.Disabled(Called(control), accessor?.PropertyName ?? locked.Name).Refused();
        return true;
    }

    /// <inheritdoc cref="Locked(object, ExpressionAccessor.Accessor?)" />
    /// <param name="control">The control one of whose handlers was sent a value.</param>
    /// <param name="bound">The expression it is bound with, read only when it refuses; null while controlled.</param>
    internal static bool Locked(object control, System.Linq.Expressions.LambdaExpression? bound) =>
        control is IUiLockable { Locked: true } && Locked(control, bound is null ? null : ExpressionAccessor.Parse(bound));

    /// <summary>Says that <paramref name="control" /> refused a value it never drew: a day outside its limits.</summary>
    /// <param name="control">The control one of whose handlers was sent the value.</param>
    /// <param name="accessor">What it is bound to, which names it in the log; null while controlled.</param>
    internal static void Unoffered(object control, ExpressionAccessor.Accessor? accessor) =>
        new OfferedOptions(Called(control), accessor?.PropertyName ?? (control as IUiLockable)?.Name).Refused();

    // The kit's name for the control, as a page opens its chain: Ui.Slider, Ui.DatePickerRange.
    private static string Called(object control)
    {
        var type = control.GetType().Name;
        var arity = type.IndexOf('`', StringComparison.Ordinal);
        var bare = arity < 0 ? type : type[..arity];
        return nameof(Ui) + "." + (bare.StartsWith(nameof(Ui), StringComparison.Ordinal) ? bare[nameof(Ui).Length..] : bare);
    }
}
