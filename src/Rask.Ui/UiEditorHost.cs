using System.Linq.Expressions;

namespace Rask;

/// <summary>
///     What a <see cref="UiComposer{T}" /> hands the <see cref="UiEditor" /> placed in its input slot: its value
///     (bound, or given with a listener), its placeholder, the lines it starts at and grows to, and its state.
/// </summary>
/// <remarks>
///     Flux's composer writes the editor <c>variant="borderless"</c> and sizes its content from <c>rows</c> and
///     <c>max-rows</c>; the kit's editor takes that from here, so it has no variant of its own to set.
/// </remarks>
internal sealed record UiEditorHost(
    Expression<Func<string?>>? Bind,
    string? Value,
    Callback<string> OnChange,
    string? Placeholder,
    int Rows,
    int MaxRows,
    bool Disabled)
{
    /// <summary>
    ///     The content's height between its lines, as Flux sizes it: a line of the content's own text each (24px on a
    ///     phone, 20px from sm up) and the 8px padding twice.
    /// </summary>
    internal string Height => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"min-height:calc({Rows}lh + 16px);max-height:calc({MaxRows}lh + 16px)");
}
