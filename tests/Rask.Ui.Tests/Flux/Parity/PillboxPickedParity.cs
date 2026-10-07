namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/pillbox</c> with two options picked in every example: what
///     <c>scripts/flux/parity-pillbox.mjs</c> holds to Flux's page once it has opened each list there and
///     clicked its second and third option.
/// </summary>
/// <remarks>
///     A page of its own because a pill is not in Flux's page as loaded, and a static page has no runtime to
///     pick with. <c>pillbox-picked</c> is no Flux slug: nothing that walks Flux's pages may assume it is one.
/// </remarks>
public sealed partial class PillboxPickedParity : PillboxParity
{
    public override string Page => "pillbox-picked";

    /// <inheritdoc />
    protected override bool Picked => true;
}
