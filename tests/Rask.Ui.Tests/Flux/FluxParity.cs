using Rask.Core;

namespace Rask.UiTests.Flux;

/// <summary>
///     One component's examples, in the order and under the section ids Flux UI's docs page gives them.
/// </summary>
/// <remarks>
///     <para>
///     Rask.Ui has to look and behave exactly as Flux does, and "exactly" is measured: <c>scripts/flux/parity.mjs</c>
///     opens Flux's live page and the page <see cref="FluxParityPages" /> writes from these examples, and
///     compares what the browser computed for each — boxes, colours, borders, shadows, and what hover, press
///     and keyboard focus change — in light and in dark.
///     </para>
///     <para>
///     So an example here is a TRANSLATION of the one on Flux's page, not a new one: the same components
///     in the same order with the same words, because the words set the widths. Its section is the id of
///     the <c>&lt;h2&gt;</c> above it there (<c>variants</c>, <c>sizes</c>, <c>button-groups</c>).
///     </para>
///     <para>
///     The kit's sheet is compiled from the kit's own sources, so a utility class written HERE is in no
///     stylesheet. Arrange an example with <see cref="Row" />, <see cref="Stack" /> or an inline style.
///     </para>
/// </remarks>
public abstract partial class FluxParity : global::Rask.Core.RaskMarkup
{
    /// <summary>The page's slug on fluxui.dev: <c>button</c> for <c>/components/button</c>.</summary>
    public abstract string Page { get; }

    public abstract IEnumerable<(string Section, Component Example)> Examples();

    /// <summary>Side by side, as most of Flux's examples are laid out.</summary>
    protected static Component Row(params Component[] items) =>
        Div.Style("display:flex;gap:16px;align-items:flex-end;justify-content:center")[items];

    /// <summary>One above the other.</summary>
    protected static Component Stack(params Component[] items) =>
        Div.Style("display:flex;flex-direction:column;gap:16px;align-items:stretch")[items];
}
