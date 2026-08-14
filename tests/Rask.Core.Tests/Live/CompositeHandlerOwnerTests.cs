using System.Text.Json;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// An event handler that closes over `this` AND a local (e.g. `() => _active = index` in a loop) is
// lowered to a compiler closure, so its delegate Target is the closure — not the component. When the
// element carrying it is nested inside a composite wrapper, the live runtime must still re-render the
// component that DEFINED the handler (via the closure's captured `this`), not the wrapper that happens
// to render the element. Without that, dogfooding interactive controls into Bs* composites silently
// drops the consumer's re-render (the CodeSample tab-switch / Action-rating regressions).
public class CompositeHandlerOwnerTests
{
    [Fact]
    public async Task HandlerCapturingThisAndLocal_NestedInComposite_RerendersDefiningComponent()
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

    // A composite wrapper that renders whatever children it is given — stands in for BsCard/BsButton.
    private sealed class PassThrough : Component
    {
        protected override Component? Render() => Div[Children ?? []];
    }
}
