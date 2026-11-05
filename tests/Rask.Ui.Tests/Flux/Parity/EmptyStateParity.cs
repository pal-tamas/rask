using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     Flux's empty state, which no component page shows: its account overview demo draws it when a search matches
///     nothing (https://fluxui.dev/demo/account-overview). <c>scripts/flux/empty-state.mjs</c> searches there and sets
///     the demo's table in an example frame; this is the kit's twin — <c>Ui.TableCell.ColSpan(2)[Ui.Empty[…]]</c> in
///     the only row of a bleeding table.
/// </summary>
public sealed partial class EmptyStateParity : FluxParity
{
    // The demo's own classes on its table (`max-sm:w-full bg-white dark:bg-zinc-900/25`), as an app's sheet holds them (Tailwind mixes the alpha in oklab),
    // under a name of their own: unlayered here, a real `.bg-white` would beat the kit's layered `dark:`.
    private const string AppUtilities =
        ".parity-empty-table{background-color:#fff}.dark .parity-empty-table{background-color:color-mix(in oklab,oklch(0.21 0.006 285.885) 25%,transparent)}"
        + "@media (width < 40rem){.parity-empty-table{width:100%}}";

    public override string Page => "empty-state";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
    [
        ("empty", Div[
            Style[Raw.Value(AppUtilities)],
            Ui.Table.Bleed().Class("parity-empty-table").Aria("label", "Transactions")[
                Ui.TableRows[
                    Ui.TableRow[Ui.TableCell.ColSpan(2)[Ui.Empty["No matching transactions"]]]
                ]
            ]
        ]),
    ];
}
