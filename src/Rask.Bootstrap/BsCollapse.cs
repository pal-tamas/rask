namespace Rask.Bootstrap;

// A Bootstrap collapse: <div class="collapse [show]">. Controlled by Open — toggling it shows/hides
// the content through the live runtime (no JS; the .show class drives display). Pair with a BsButton
// that flips your Open state.
public sealed partial class BsCollapse : BsBlock
{
    public bool? Open { get; set; }
    public bool? Horizontal { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join(
            "collapse",
            Horizontal is true ? "collapse-horizontal" : null,
            Open is true ? "show" : null,
            Class))[Items];
}
