namespace Rask.Bootstrap;

// A Bootstrap collapse: <div class="collapse [show]">. Controlled by Open — toggling it shows/hides
// the content through the live runtime (no JS; the .show class drives display). Pair with a BsButton
// that flips your Open state.

/// <summary>
///     A region that expands and collapses. The control that toggles it should carry <c>aria-expanded</c>
///     so its state is announced.
/// </summary>
public sealed partial class BsCollapse : BsBlock
{
    /// <summary>Whether the region is expanded.</summary>
    public bool? Open { get; set; }

    /// <summary>Collapses along the width rather than the height.</summary>
    public bool? Horizontal { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join(
            "collapse",
            Horizontal is true ? "collapse-horizontal" : null,
            Open is true ? "show" : null,
            Class))[Items];
}
