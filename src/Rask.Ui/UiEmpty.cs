namespace Rask;

/// <summary>
///     What a list says when it has nothing to show — one sentence, centred, said to a screen reader as it changes:
///     <c>Ui.Empty["No suppliers yet."]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Not a Flux component: Flux has no empty state of its own. It is the one Flux's own demo draws (fluxui.dev's
///         account overview, a search that matches nothing): a <c>&lt;div role="status"&gt;</c> with 24px above and
///         below the centred line, in the only cell of a row as wide as the table — which is what
///         <c>Ui.TableCell.ColSpan(columns)[Ui.Empty[…]]</c> writes, and what a <see cref="UiTable" /> drawn from a query
///         shows when it found nothing.
///     </para>
///     <para>
///         In place of a table it reads the same: the cell's 14px and muted ink (zinc-500, zinc-300 in dark) are its
///         own, at no weight, so a class of the call site's wins.
///     </para>
/// </remarks>
public sealed partial class UiEmpty : UiElement
{
    // Measured on Flux's demo (light and dark): 24px above and below, centred, and the ink and size of the cell it sits in.
    private const string Look =
        "[:where(&)]:py-6 [:where(&)]:text-center [:where(&)]:text-sm [:where(&)]:text-zinc-500 [:where(&)]:dark:text-zinc-300";

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(Look, Class);

    /// <inheritdoc />
    /// <remarks>A status, as Flux's demo writes it: a screen reader says the sentence when a filter empties the list.</remarks>
    private protected override string? ResolveRole() => Role ?? "status";
}
