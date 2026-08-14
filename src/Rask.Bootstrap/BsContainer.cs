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
public sealed partial class BsContainer : BsBlock
{
    public bool? Fluid { get; set; }
    public Bp? FluidBelow { get; set; }

    protected override Component? Render() => Wrap(
        FluidBelow is { } bp ? Grid.ContainerBelow(bp)
        : Fluid is true ? Grid.ContainerFluid
        : Grid.Container);
}
