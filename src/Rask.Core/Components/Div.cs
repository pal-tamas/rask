using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Div : Element
{
    protected override string TagName => "div";

    public Callback? OnClick { get; set; }
    public CallbackAsync? OnClickAsync { get; set; }

    // Bound to the element's `scroll` event by the client runtime (data-rask-on-scroll). Ships a
    // sync `OnScroll` (Action<ScrollEvent>) and an async `OnScrollAsync` (Func<ScrollEvent, Task>)
    // sibling — the typed-pair convention shared with OnClick/OnClickAsync; set at most one. The two
    // are typed views over a single backing slot (distinguished by delegate type, like Element's
    // drag/keyboard pairs) so the pair adds no extra per-instance field. The dispatcher unpacks the
    // {scrollTop, clientHeight, scrollHeight} payload into a typed ScrollEvent before invoking.
    private Delegate? _onScroll;

    public Callback<ScrollEvent>? OnScroll
    {
        get => _onScroll as Callback<ScrollEvent>;
        set
        {
            if (value is not null)
            {
                _onScroll = value;
            }
            else if (_onScroll is Callback<ScrollEvent>)
            {
                _onScroll = null;
            }
        }
    }

    public CallbackAsync<ScrollEvent>? OnScrollAsync
    {
        get => _onScroll as CallbackAsync<ScrollEvent>;
        set
        {
            if (value is not null)
            {
                _onScroll = value;
            }
            else if (_onScroll is CallbackAsync<ScrollEvent>)
            {
                _onScroll = null;
            }
        }
    }

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

        if (_onScroll is not null)
        {
            AppendAttr(sb, "data-rask-on-scroll", ctx.RegisterHandler(_onScroll));
        }
    }
}
