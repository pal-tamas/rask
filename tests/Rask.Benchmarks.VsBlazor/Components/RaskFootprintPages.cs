using Rask.Core;

namespace Rask.Benchmarks.VsBlazor.Components;

// User-component pages for the retained-footprint report — the production shape (a real Rask page IS
// a user component whose Render() builds the DOM), and apples-to-apples with the Blazor side's
// ComponentBase. Neither caches its subtree in a field: the built tree lives only in the render
// cache, so Phase B's clean-subtree frame cache snapshots it and RELEASES the Element object graph.
// Both render pure elements with no handlers, no page-level Key, and no nested user components, so
// they are cache-eligible.
#pragma warning disable RASK014 // benchmark-internal components, constructed directly in the report
public sealed partial class RaskLargePage : Component
{
    protected override Component? Render() => LargePageWithCounter.BuildRask(0);
}

public sealed partial class RaskKeyedListPage : Component
{
    protected override Component? Render()
    {
        var order = new int[100];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        return KeyedList.BuildRask(order);
    }
}
#pragma warning restore RASK014
