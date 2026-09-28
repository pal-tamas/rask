using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Components;

/// <summary>
///     Renders the validation errors recorded for one bound field — the message half of a form, where
///     <c>Input.Bind</c> is the binding half. Renders nothing while the field is valid.
/// </summary>
[RaskChainGroup(typeof(global::Rask.Validation))]
public sealed partial class ValidationMessage : Component
{
    /// <summary>
    ///     The field whose errors to show, as the same expression the control was bound to — <c>() =>
    ///     model.Email</c>.
    /// </summary>
    public LambdaExpression? For { get; set; }

    // Headless: caller owns the markup. Invoked only when at least one message exists
    // for the bound field; the empty case renders nothing.

    /// <summary>
    ///     Your own markup for the errors, given the messages. Without it, a default is rendered.
    /// </summary>
    public required Func<IReadOnlyList<string>, Component> Template { get; set; }

    // No manual BypassRenderCache: reading EditContext.GetValidationMessages in Render() auto-latches
    // the render-cache opt-out (see EditContext.MarkReader / Component._readsAmbientState), so a message
    // added by a later (e.g. post-await) render is always observed instead of served stale from cache.

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
