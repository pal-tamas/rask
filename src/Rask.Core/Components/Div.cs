using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Div : Element
{
    protected override string TagName => "div";

    public Action? OnClick { get; set; }
    public Func<Task>? OnClickAsync { get; set; }

    // Bound to the element's `scroll` event by the client runtime (data-rask-on-scroll).
    // Accepts Action<ScrollEvent> or Func<ScrollEvent, Task>; the dispatcher unpacks the
    // {scrollTop, clientHeight, scrollHeight} payload into a typed ScrollEvent before invoking.
    public Delegate? OnScroll { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null)
        {
            AppendAttr(sb, "data-rask-on-click", ctx.RegisterHandler(click));
        }

        if (OnScroll is not null)
        {
            AppendAttr(sb, "data-rask-on-scroll", ctx.RegisterHandler(OnScroll));
        }
    }
}
