using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK014 // test host/consumer components have no generated factories

namespace Rask.Bootstrap.Tests;

// A BOUND wrapper form control (BsCheck) used OUTSIDE a Form must re-render the component that AUTHORED
// the binding — even when the bind closed over a loop local (`() => item.Done`, whose expression root is
// a closure, not a component) — so a sibling that derives from the same model property refreshes.
//
// Regression for the Todos checkbox: a wrapper control re-renders only itself, so the framework records
// the control's creating parent (the authoring component) as the bind owner and re-renders it on a
// two-way write. Without the fix the sibling's derived class stayed stale.
public class BsBoundOutOfFormRerenderTests
{
    [Fact]
    public async Task BoundCheck_OutsideForm_LoopLocalBind_RerendersAuthoringConsumer()
    {
        var host = new Host();

        var html = host.RenderAsLiveRoot();
        Assert.Equal(1, host.Consumer.RenderCount);
        Assert.Contains("class=\"todo\"", html);

        // Toggle the checkbox (checkboxes report the absolute "true"/"false" state).
        var changeId = Markup.Attr(html, "data-rask-on-change");
        Assert.NotNull(changeId);
        using var doc = JsonDocument.Parse("{\"value\":\"true\"}");
        Assert.True(await host.TryInvokeHandlerAsync(changeId!, doc.RootElement));

        var after = host.RenderAsLiveRoot();
        // The authoring consumer (render-cached by the host) re-rendered, and its derived sibling flipped.
        Assert.Equal(2, host.Consumer.RenderCount);
        Assert.Contains("class=\"done\"", after);
    }

    // Render-caches the consumer (stable props) so the bug — failing to dirty the consumer — is
    // observable; a fresh root render alone would mask it.
    private sealed class Host : Component
    {
        public readonly Consumer Consumer = new();

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => Consumer);
            ctx.NotifyParameters(c, false);
            return Div()[c];
        }
    }

    private sealed class Consumer : Component
    {
        private readonly Item _item = new();
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            var item = _item; // local ⇒ the bind root is a closure, not `this` (mirrors a foreach loop var)
            return Div()[
                BsCheck(() => item.Done, Id: "chk"),
                Span(Class: item.Done ? "done" : "todo")["x"]
            ];
        }

        private sealed class Item
        {
            public bool Done { get; set; }
        }
    }
}
