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

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;

        if (LiveRenderContext.Current is not { } ctx)
        {
            yield break;
        }

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null)
        {
            yield return new("data-rask-on-click", ctx.RegisterHandler(click));
        }

        if (OnScroll is not null)
        {
            yield return new("data-rask-on-scroll", ctx.RegisterHandler(OnScroll));
        }
    }
}
