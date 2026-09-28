using Rask.Core.Forms;

namespace Rask.Core.Components;

/// <summary>
///     Renders the validation errors recorded for one bound field — the message half of a form, where
///     <c>Input.Bind</c> is the binding half. Renders nothing while the field is valid.
/// </summary>
[RaskChainGroup(typeof(global::Rask.Validation))]
public sealed partial class ValidationSummary : Component
{
    // Headless: caller owns the markup. Invoked only when the form has at least one
    // message; each entry pairs the offending field name (empty for form-level messages)
    // with its error text.

    /// <summary>
    ///     Your own markup for the errors, given the messages. Without it, a default is rendered.
    /// </summary>
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

        return Template!(entries);
    }
}
