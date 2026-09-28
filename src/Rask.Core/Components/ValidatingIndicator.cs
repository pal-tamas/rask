using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

/// <summary>
///     Renders the validation errors recorded for one bound field — the message half of a form, where
///     <c>Input.Bind</c> is the binding half. Renders nothing while the field is valid.
/// </summary>
[RaskChainGroup(typeof(global::Rask.Validation), "Indicator")]
public sealed partial class ValidatingIndicator : Component
{
    // After EditContext.IsValidating(field) flips back to false, keep the
    // template rendered for ValidatingStickinessMs after the last
    // PendingCount > 0 reading. Smooths out very-short validation windows (a
    // 400ms async check would otherwise leave just a ~400ms DOM presence —
    // too brief for screen-readers / load-balanced Playwright polling to
    // reliably catch). The sticky state lives on the EditContext's FieldState
    // (see <see cref="EditContext.IsValidating(FieldIdentifier)" />) so it
    // survives the generic factory's per-render `new()` instantiation; the
    // EditContext also schedules a single timer-driven dismissal render at
    // sticky-window expiry.

    /// <summary>
    ///     The field whose errors to show, as the same expression the control was bound to — <c>() =>
    ///     model.Email</c>.
    /// </summary>
    public LambdaExpression? For { get; set; }

    /// <summary>
    ///     Your own markup for the errors, given the messages. Without it, a default is rendered.
    /// </summary>
    public required Func<Component> Template { get; set; }

    // Reads EditContext.ShouldShowValidatingIndicator(field) in Render() — auto-latches the cache
    // opt-out; see ValidationMessage for the rationale.

    protected override Component? Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null || For is null)
        {
            return new Fragment();
        }

        var acc = ExpressionAccessor.Parse(For);
        return ctx.ShouldShowValidatingIndicator(acc.Field) ? Template() : new Fragment();
    }
}
