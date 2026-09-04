namespace Rask.Example.Shared.Features;

// Tier 2 — a stateful Component: it keeps local state in a private field and mutates it in a
// handler. The OnClick lambda captures `this`, so after it runs the framework re-renders this
// component automatically — no StateHasChanged() call needed. (You only reach for
// StateHasChanged() when the mutation happens off the event-dispatch path, e.g. a background
// poll loop — see docs/lifecycle.md.)
public sealed partial class TierStatefulCounterDemo : Component
{
    private int _count;

    protected override Component? Render() =>
        Button.Type("button").Class(Tw.BtnPrimary).OnClick(() => _count++)[
            $"Clicked {_count} times"
        ];
}
