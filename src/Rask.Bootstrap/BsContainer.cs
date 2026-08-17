namespace Rask.Bootstrap;

// A Bootstrap layout container: <div class="container">. The outermost wrapper of a page — it centres the
// content, caps its width per breakpoint, and pads its own sides by half a gutter, which is what a nested
// BsRow's negative side margins cancel against.
//
//   BsContainer()                    → <div class="container">        width-capped at every breakpoint
//   BsContainer(Fluid: true)         → <div class="container-fluid">  full width at every breakpoint
//   BsContainer(FluidBelow: Bp.Md)   → <div class="container-md">     full width below md, capped from md up
//
// FluidBelow is named for what it does, not for the class it emits: Bootstrap's .container-md reads as
// "a container at md" but is really the fluid one below md (it picks up a max-width only from md up). It
// supersedes Fluid when both are set — the same precedence BsModal.FullscreenBelow has over Fullscreen.

/// <summary>
///     The outermost layout wrapper, which centres content and applies the responsive gutters every
///     <c>BsRow</c> inside it assumes.
/// </summary>
public sealed partial class BsContainer : BsBlock
{
    /// <summary>
    ///     Spans the full viewport width at every breakpoint rather than stepping down through fixed
    ///     widths.
    /// </summary>
    public bool? Fluid { get; set; }

    /// <summary>Spans the full width below this breakpoint and becomes fixed-width above it.</summary>
    public Bp? FluidBelow { get; set; }

    protected override Component? Render() => Wrap(
        FluidBelow is { } bp ? Grid.ContainerBelow(bp)
        : Fluid is true ? Grid.ContainerFluid
        : Grid.Container);
}
