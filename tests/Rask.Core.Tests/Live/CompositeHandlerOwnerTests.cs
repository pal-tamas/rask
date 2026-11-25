using System.Text.Json;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// An event handler that closes over `this` AND a local (e.g. `() => _active = index` in a loop) is
// lowered to a compiler closure, so its delegate Target is the closure — not the component. When the
// element carrying it is nested inside a composite wrapper, the live runtime must still re-render the
// component that DEFINED the handler (via the closure's captured `this`), not the wrapper that happens
// to render the element. Without that, dogfooding interactive controls into Bs* composites silently
// drops the consumer's re-render (the CodeSample tab-switch / Action-rating regressions).
public partial class CompositeHandlerOwnerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_handler_capturing_this_and_a_local_nested_in_a_composite_rerenders_the_defining_component()
    {
        var owner = new TabOwner();
        var view = new StubComponent(() => owner);

        var html1 = view.RenderAsLiveRoot();
        Assert.Contains("active=0", html1);

        // Click the "tab2" button — its handler is `() => Active = 2`, defined in TabOwner but rendered
        // inside the PassThrough composite. The defining component (TabOwner) must re-render.
        var clickIds = System.Text.RegularExpressions.Regex
            .Matches(html1, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(2, clickIds.Count); // one per tab button
        using var payload = JsonDocument.Parse("{}");
        await view.TryInvokeHandlerAsync(clickIds[1], payload.RootElement); // the tab2 handler

        Assert.Equal(2, owner.Active);
        var html2 = view.RenderAsLiveRoot();
        Assert.Contains("active=2", html2); // TabOwner re-rendered through the composite
    }

    [Fact]
    public async Task A_handler_capturing_only_a_local_list_nested_in_a_composite_rerenders_the_defining_component()
    {
        var owner = new RowsOwner();
        var view = new StubComponent(() => owner);
        var before = view.RenderAsLiveRoot();
        var addId = System.Text.RegularExpressions.Regex.Match(before, "data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        using var payload = JsonDocument.Parse("{}");

        await view.TryInvokeHandlerAsync(addId, payload.RootElement);
        var after = view.RenderAsLiveRoot();

        Assert.Contains("rows=0", before);
        Assert.Contains("rows=1", after);
        Assert.Contains("row-1", after);
    }

    [Fact]
    public async Task A_callback_capturing_only_a_local_list_handed_to_a_component_rerenders_the_defining_component()
    {
        var owner = new RelayedRowsOwner();
        var view = new StubComponent(() => owner);
        var before = view.RenderAsLiveRoot();
        var addId = System.Text.RegularExpressions.Regex.Match(before, "data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        using var payload = JsonDocument.Parse("{}");

        await view.TryInvokeHandlerAsync(addId, payload.RootElement);
        var after = view.RenderAsLiveRoot();

        Assert.Contains("rows=0", before);
        Assert.Contains("rows=1", after);
        Assert.Contains("row-1", after);
    }

    private sealed class TabOwner : Component
    {
        public int Active;

        protected override Component? Render()
        {
            var kids = new List<Component> { Span[$"active={Active}"] };
            for (var i = 1; i <= 2; i++)
            {
                var index = i; // captured local → handler becomes a closure, not a this-bound method
                kids.Add(Button.Key(index).OnClick(() => Active = index)[$"tab{index}"]);
            }

            return [new PassThrough()[kids]];
        }
    }

    // The handler closes over a local copy of the list and nothing of the component: its delegate's target is a
    // closure with no captured `this`, so the component that owns the list cannot be read off the delegate.
    private sealed class RowsOwner : Component
    {
        public List<string> Rows { get; } = [];

        protected override Component? Render()
        {
            var rows = Rows;
            var kids = new List<Component>
            {
                Span[$"rows={rows.Count}"],
                Button.OnClick(() => rows.Add($"row-{rows.Count + 1}"))["add"],
            };
            kids.AddRange(rows.Select(row => (Component)Span.Key(row)[row]));
            return [new PassThrough()[kids]];
        }
    }

    // The same list, added to through a component's event: the callback is wrapped as a generated chain step wraps
    // it (AutoCallback), while this component renders.
    private sealed class RelayedRowsOwner : Component
    {
        public List<string> Rows { get; } = [];

        protected override Component? Render()
        {
            var rows = Rows;
            var kids = new List<Component>
            {
                Span[$"rows={rows.Count}"],
                new ClickRelay { OnClick = new Callback(AutoCallback.Wrap(() => rows.Add($"row-{rows.Count + 1}"))!) },
            };
            kids.AddRange(rows.Select(row => (Component)Span.Key(row)[row]));
            return Div[kids];
        }
    }

    // A component with an event of its own, fired from its button — what a kit button is.
    private sealed class ClickRelay : Component
    {
        public Callback OnClick { get; set; }

        protected override Component? Render() => Button.OnClick(() => OnClick.Invoke())["add"];
    }

    // A composite wrapper that renders whatever children it is given — stands in for BsCard/BsButton.
    private sealed class PassThrough : Component
    {
        protected override Component? Render() => Div[Children ?? []];
    }
}
