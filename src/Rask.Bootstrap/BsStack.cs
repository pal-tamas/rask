namespace Rask.Bootstrap;

// A flex stack: <div class="d-flex">. The one-line answer to "lay these out in a line with a gap" —
// horizontal by default, set Vertical for a column (matching BsButtonGroup's Vertical flag).
//
//   BsStack(Gap: 2)[BsButton()["Save"], BsButton()["Cancel"]]   → <div class="d-flex gap-2">
//   BsStack(Vertical: true, Gap: 3)[…]                          → <div class="d-flex flex-column gap-3">
//   BsStack(Gap: 2, Align: BsAlign.Center)[…]                   → <div class="d-flex gap-2 align-items-center">
//
// It builds on d-flex rather than Bootstrap's .vstack/.hstack shorthands, deliberately: neither is a
// superset of d-flex, so building on them would silently restyle every plain d-flex it replaced.
// .hstack is not "d-flex" — it also sets align-items:center and align-self:stretch — and .vstack is not
// "d-flex flex-column", it also sets flex:1 1 auto and align-self:stretch. Align says the alignment out
// loud instead, and otherwise the CSS default is left alone. (So going the other way is not a pure
// rename: align-self:stretch is the part BsStack never emits — it only bites when the stack is itself a
// flex item, and Class: "align-self-stretch" / Flex.Fill restore it.) Bootstrap also ships no responsive
// variant of either shorthand, while .flex-md-row exists — so responsive direction only composes on this
// base: BsStack(Vertical: true, Class: Flex.Row(Bp.Md)) is a column that becomes a row at md.
//
// Horizontal emits no flex-row token, because row is already the flex default — which keeps
// BsStack(Gap: 2) byte-identical to the "d-flex gap-2" it replaces.
public sealed class BsStack : BsBlock
{
    public bool? Vertical { get; set; }
    public int? Gap { get; set; }
    public BsJustify? Justify { get; set; }
    public BsAlign? Align { get; set; }

    // Lets the items flow onto more lines (.flex-wrap). Named for whose wrapping it controls — the stack's
    // items, not the stack itself. (It also has to be: a Wrap property would hide BsBlock's Wrap(string)
    // helper, which is CS0108 and this repo builds warnings-as-errors.)
    public bool? WrapItems { get; set; }

    protected override Component? Render() => Div(
        Id: Id,
        Class: BsClass.Join(
            Display.Flex(),
            Vertical is true ? Flex.Column() : null,
            Gap is { } gap ? Flex.Gap(gap) : null,
            Justify is { } justify ? Flex.Justify(justify) : null,
            Align is { } align ? Flex.Align(align) : null,
            WrapItems is true ? Flex.Wrap() : null,
            Class))[Items];
}
