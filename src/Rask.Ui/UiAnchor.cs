namespace Rask;

/// <summary>
///     Where a list of options opens against the control it hangs from, by CSS anchor positioning: the
///     style of a popover anchored to <c>anchor-name: --name</c>.
/// </summary>
/// <remarks>
///     In a style attribute, which Tailwind does not scan, so the values are built freely. The list is as wide
///     as its anchor (a class's <c>min-width</c> may widen it), five pixels away — Flux's gap — and flips to
///     the other side when it would not fit. An engine without anchor positioning keeps the popover's own
///     default, centred: still open and usable.
/// </remarks>
internal static class UiAnchor
{
    /// <summary>The popover's placement style.</summary>
    /// <param name="name">The anchor's name, without its dashes.</param>
    /// <param name="position">The side it opens on. Below when unset.</param>
    /// <param name="align">The edge it lines up with. The start when unset.</param>
    internal static string Under(string name, Ui.Position? position, Ui.Align? align)
    {
        // `inset: auto` first: a popover's own is 0 on every side. Each side is then stated against the anchor,
        // which is what lets `flip-block` turn "five pixels under" into "five pixels over" by itself.
        var across = position switch
        {
            Ui.Position.Top => "bottom:calc(anchor(top) + 5px)",
            Ui.Position.Left => "right:calc(anchor(left) + 5px)",
            Ui.Position.Right => "left:calc(anchor(right) + 5px)",
            _ => "top:calc(anchor(bottom) + 5px)",
        };
        var beside = position is Ui.Position.Left or Ui.Position.Right;
        var along = align switch
        {
            Ui.Align.Center => beside ? "align-self:anchor-center" : "justify-self:anchor-center",
            Ui.Align.End => beside ? "bottom:anchor(bottom)" : "right:anchor(right)",
            _ => beside ? "top:anchor(top)" : "left:anchor(left)",
        };

        return "position-anchor:--" + name
               + ";inset:auto;margin:0;" + across + ";" + along
               + ";position-try-fallbacks:flip-block,flip-inline"
               + ";width:anchor-size(width)";
    }
}
