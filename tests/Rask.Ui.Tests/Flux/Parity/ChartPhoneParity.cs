namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <see cref="ChartParity" /> on a phone: the same examples, each chart drawn at the box Flux's phone frame gives
///     it, for <c>parity.mjs chart --view 390x844</c> and <c>parity-chart.mjs --view 390x844</c>.
/// </summary>
/// <remarks>
///     A chart draws at the size its page reports back to the server. A parity page is static, so there is no
///     server to draw it again at a phone's width: it is drawn for that width here.
/// </remarks>
public sealed class ChartPhoneParity : ChartParity
{
    public override string Page => "chart@390x844";

    protected override string View => "390x844";
}
