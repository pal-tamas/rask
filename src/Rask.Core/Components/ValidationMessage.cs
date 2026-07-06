using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

public sealed class ValidationMessage : Component
{
    public LambdaExpression? For { get; set; }

    // Headless: caller owns the markup. Invoked only when at least one message exists
    // for the bound field; the empty case renders nothing.
    public required Func<IReadOnlyList<string>, Component> Template { get; set; }

    // No manual BypassRenderCache: reading EditContext.GetValidationMessages in Render() auto-latches
    // the render-cache opt-out (see EditContext.MarkReader / Component._consumesContext), so a message
    // added by a later (e.g. post-await) render is always observed instead of served stale from cache.

    [GenerateForwarderFactory]
    public static ValidationMessage Bound<TProp>(
        Expression<Func<TProp>> For,
        Func<IReadOnlyList<string>, Component> Template) =>
        new() { For = For, Template = Template };

    protected override Component? Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null || For is null)
        {
            return new Fragment();
        }

        var acc = ExpressionAccessor.Parse(For);
        var msgs = ctx.GetValidationMessages(acc.Field);
        if (msgs.Count == 0)
        {
            return new Fragment();
        }

        return Template(msgs);
    }
}

public sealed class ValidationSummary : Component
{
    // Headless: caller owns the markup. Invoked only when the form has at least one
    // message; each entry pairs the offending field name (empty for form-level messages)
    // with its error text.
    public required Func<IReadOnlyList<ValidationEntry>, Component?> Template { get; set; }

    // Reads EditContext.GetValidationEntries in Render() — auto-latches the cache opt-out; see
    // ValidationMessage for the rationale.

    protected override Component? Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null)
        {
            return null;
        }

        var entries = ctx.GetValidationEntries();
        if (entries.Count == 0)
        {
            return null;
        }

        return Template(entries);
    }
}

public sealed class ValidatingIndicator : Component
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
    public LambdaExpression? For { get; set; }

    public required Func<Component> Template { get; set; }

    // Reads EditContext.ShouldShowValidatingIndicator(field) in Render() — auto-latches the cache
    // opt-out; see ValidationMessage for the rationale.

    [GenerateForwarderFactory]
    public static ValidatingIndicator Bound<TProp>(
        Expression<Func<TProp>> For,
        Func<Component> Template) =>
        new() { For = For, Template = Template };

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
