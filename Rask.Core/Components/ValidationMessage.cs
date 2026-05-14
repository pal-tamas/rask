using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

public sealed class ValidationMessage : Component
{
    public LambdaExpression? For { get; set; }

    // Headless: caller owns the markup. Invoked only when at least one message exists
    // for the bound field; the empty case renders nothing.
    public required Func<IReadOnlyList<string>, Component> Template { get; set; }

    // Reads mutable EditContext state (per-field message list) the framework doesn't observe.
    // The cache would otherwise pin the messages seen during whichever earlier render
    // populated it — e.g. the mid-await render captures the no-message state, and the post-
    // handler render then reuses that stale subtree even after an async validator added a
    // message. Same rationale as Router/Outlet opting out for RouteState.
    protected internal override bool BypassRenderCache => true;

    [GenerateForwarderFactory]
    public static ValidationMessage Bound<TProp>(
        Expression<Func<TProp>> For,
        Func<IReadOnlyList<string>, Component> Template) =>
        new() { For = For, Template = Template };

    protected override Component Render()
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
    public required Func<IReadOnlyList<ValidationEntry>, Component> Template { get; set; }

    // Reads EditContext message state — see ValidationMessage for the rationale.
    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null)
        {
            return new Fragment();
        }

        var entries = ctx.GetValidationEntries();
        if (entries.Count == 0)
        {
            return new Fragment();
        }

        return Template(entries);
    }
}

public sealed class ValidatingIndicator : Component
{
    public LambdaExpression? For { get; set; }
    public string? Class { get; set; }

    // Reads EditContext.IsValidating(field) — see ValidationMessage for the rationale.
    protected internal override bool BypassRenderCache => true;

    [GenerateForwarderFactory]
    public static ValidatingIndicator Bound<TProp>(
        Expression<Func<TProp>> For,
        string? Class = null) => new() { For = For, Class = Class };

    protected override Component Render()
    {
        var ctx = EditContextScope.Current;
        if (ctx is null || For is null)
        {
            return new Fragment();
        }

        var acc = ExpressionAccessor.Parse(For);
        if (!ctx.IsValidating(acc.Field))
        {
            return new Fragment();
        }

        return Components.Span(Class: Class ?? "validating-indicator")[Children ?? Array.Empty<Child>()];
    }
}