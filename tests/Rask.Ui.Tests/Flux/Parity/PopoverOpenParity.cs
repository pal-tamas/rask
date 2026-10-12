using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     The popover page again with each popover OPEN — which is the component, and what the page as it loads
///     never shows.
/// </summary>
/// <remarks>
///     One example per dropdown, eleven in all: the example it sits in on Flux's page, with that one trigger
///     marked <c>data-parity-open</c> for the measurer to press (or to hover, for the hover card). Nothing on
///     the page opens it but the browser — there is no runtime here — so a match is also the proof that the
///     popover works without one. <c>scripts/flux/parity-popover.mjs</c> measures Flux's page the same way and
///     compares the two.
/// </remarks>
public sealed partial class PopoverOpenParity : FluxParity
{
    public override string Page => "popover-open";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
        Enumerable.Range(0, PopoverParity.Dropdowns)
            .Select(dropdown => ("open", PopoverParity.Example(PopoverParity.ExampleOf(dropdown), dropdown)));
}
