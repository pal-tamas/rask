namespace Rask.Bootstrap;

// Base for EVERY Bootstrap component. This is the enforced library convention:
//
//   1. Bs components WRAP the core components — their Render() composes Div()/Span()/Button()/… with
//      Bootstrap classes. They never subclass Element to mint a new element type (an architecture
//      test asserts no public Bs* type derives from Element directly).
//   2. Inside a Bs component, prefer another Bs component when one exists (BsModal/BsAlert/BsOffcanvas/
//      BsToast reuse BsCloseButton; BsDropdown reuses BsButton) instead of re-emitting raw classes.
//   3. Event delegates are forwarded straight to the native component (the consumer's handler closes
//      over their page, so handler-owner resolution re-renders it) — both the sync and async params
//      are forwarded; RASK027 is suppressed in this layer only (see .editorconfig).
//   4. Prefer the inline `cond ? node : (Child)Fragment()` shape over building List<Child>.
//
// BsBlock itself exposes the Id/Class pass-through that the generated factory surfaces as optional
// parameters, without pulling in Element's full HTML attribute/event surface. Abstract, so the
// factory generator skips it; subclasses inherit Id/Class as leading optional factory params.
public abstract class BsBlock : Component
{
    public string? Id { get; set; }
    public string? Class { get; set; }

    // These wrappers replace inline core elements (Div/Span/…) and frequently carry children that the
    // parent changes dynamically (e.g. a conditional alert) while their own props (Id/Class) stay the
    // same. The render cache memoises on factory-param changes only — Children comes through the indexer,
    // not a param — so without this a dynamic child update would be dropped (the inline Element it
    // replaced never cached). Re-rendering a thin wrapper every walk is cheap and restores that parity.
    protected override bool BypassRenderCache => true;

    // The children passed via the indexer, or an empty sequence.
    private protected IEnumerable<Child> Items => Children ?? [];

    // Renders a <div class="{baseClass} {Class}" id="{Id}"> wrapper around the children — the shape
    // most container parts share (card sections, etc.).
    private protected RenderResult Wrap(string baseClass) =>
        Div(Id: Id, Class: BsClass.Join(baseClass, Class))[Items];

    // The children followed by extra trailing children (e.g. an alert's close button), as one
    // sequence for the children indexer (the `..` spread is unsupported — pass an enumerable).
    private protected IEnumerable<Child> ItemsWith(params Child[] trailing)
    {
        foreach (var item in Items)
        {
            yield return item;
        }

        foreach (var item in trailing)
        {
            yield return item;
        }
    }
}
