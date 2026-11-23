using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Which control a form draws each bound member with when no <see cref="UiField" /> is around it — a table cell's
///     <c>Ui.Input.Bind(() =&gt; row.Size)</c> — so a <c>Ui.Error.For(() =&gt; row.Size)</c> beside it takes the id the
///     control's <c>aria-describedby</c> names, as Flux's <c>flux:error name</c> sits beside its input.
/// </summary>
/// <remarks>
///     Kept per form, by the member's <see cref="FieldIdentifier" /> — the row's model and the member's name — so each
///     row of a table finds its own control. The first control to claim a member keeps it: a composite control claims
///     it before the parts it draws inside itself. Bounded by the members the form binds, and gone with the form.
/// </remarks>
internal static class UiBareControls
{
    private static readonly ConditionalWeakTable<EditContext, Dictionary<FieldIdentifier, string>> Forms = [];

    /// <summary>
    ///     Claims the member <paramref name="bound" /> reads for <paramref name="controlId" /> in the form being drawn,
    ///     unless another control holds it, and says whether this control is the one that does.
    /// </summary>
    internal static bool Claim(LambdaExpression bound, string controlId) =>
        EditContextScope.Current is { } form
        && string.Equals(
            CollectionsMarshal.GetValueRefOrAddDefault(Forms.GetOrCreateValue(form), ExpressionAccessor.Parse(bound).Field, out _) ??= controlId,
            controlId,
            StringComparison.Ordinal);

    /// <summary>The id of the control drawing <paramref name="field" /> in the form being drawn, or null.</summary>
    internal static string? ControlOf(FieldIdentifier field) =>
        EditContextScope.Current is { } form && Forms.TryGetValue(form, out var controls) && controls.TryGetValue(field, out var id)
            ? id
            : null;
}
