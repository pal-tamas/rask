using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     Flux's command palette once it is used, which a page as loaded never shows: <c>scripts/flux/command-open.mjs</c>
///     types into the first two and opens the third on this page and on Flux's (whose first example it copies twice),
///     the same way, before <c>parity.mjs</c> compares them.
/// </summary>
/// <remarks>
///     <c>typed</c>: "ch" typed and the box left — one command shown and the clear button. <c>empty</c>: "zz" typed —
///     no command, Flux's "No results found". <c>as-a-modal</c>: the palette opened from its trigger, the box pressed
///     and ArrowDown — the second command lit, the close button in the corner.
/// </remarks>
public sealed partial class CommandOpenParity : FluxParity
{
    public override string Page => "command-open";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        var typed = new CommandParity().Examples().ToList();

        return [("typed", typed[0].Example), ("empty", new CommandParity().Examples().First().Example), ("as-a-modal", typed[1].Example)];
    }
}
