using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// A component's async callback must be able to show an INTERMEDIATE state — the spinner around a fetch —
// without the consumer reaching for StateHasChanged.
//
// Rask already renders mid-await: when a handler's task actually yields, InvokeWithRenderingAsync renders
// through the HandlerSyncContext. But that render serves any clean component from the render cache, so it
// only shows the intermediate state if the component whose state changed is marked dirty BEFORE the await.
//
// The DOM-handler path always did that (TryInvokeHandlerAsync: "Set BEFORE running so intermediate renders
// inside an async handler already see the owner as dirty"). AutoCallback — which is what carries a
// parent-supplied delegate into a child component — marked the owner dirty only AFTER awaiting. So an async
// Button(OnClickAsync:) could paint mid-flight and the identical BsDataGrid(OnSortChangeAsync:) could not:
// the consumer's `_loading = true` was invisible, and the spinner it drove appeared only after the work it
// was meant to cover had finished. These pin both halves of the window.
public partial class AsyncCallbackMidAwaitRenderTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task State_set_mid_await_is_rendered_without_StateHasChanged()
    {
        var host = new Host();
        host.RenderHandle = new RenderingHandle(host);

        var html = host.RenderAsLiveRoot(RenderHarness.EmptyServices());
        Assert.Contains("state: idle", html, StringComparison.Ordinal);

        // Fire the child's button; the consumer's callback sets "busy" and then awaits the gate.
        var clickId = Markup.Attr(html, "data-rask-on-click");
        using var doc = JsonDocument.Parse("{}");
        var dispatch = host.TryInvokeHandlerAsync(clickId!, doc.RootElement);

        // Suspended inside the await. This is the whole point: the consumer never called StateHasChanged,
        // yet its intermediate state must already be on screen.
        await host.Consumer.Started.Task;
        Assert.Contains("state: busy", host.LastHtml, StringComparison.Ordinal);

        host.Consumer.Release.SetResult();
        await dispatch;

        // ...and the post-handler render clears it.
        Assert.Contains("state: done", host.RenderAsLiveRoot(RenderHarness.EmptyServices()),
            StringComparison.Ordinal);
    }

    // Re-renders on request and records the markup, so the test can read what the mid-await render produced.
    //
    // One render at a time, as a live session's render gate guarantees. Without it the callback resuming on the pool
    // could ask for a render while the handler drain was still walking the same tree, and the second walk mutated the
    // alive sets the first was enumerating: "Collection was modified", once in four full-suite runs (#1104). A Monitor
    // rather than a semaphore, so a render requested from inside a render on the same thread still goes through, as it
    // did before; only a render from another thread waits.
    private sealed class RenderingHandle(Host view) : IRenderHandle
    {
        private readonly Lock _gate = new();

        public Task RequestRenderAsync()
        {
            Render();
            return Task.CompletedTask;
        }

        Task IRenderHandle.RenderInScopeAsync()
        {
            Render();
            return Task.CompletedTask;
        }

        private void Render()
        {
            lock (_gate)
            {
                view.LastHtml = view.RenderAsLiveRoot(RenderHarness.EmptyServices());
            }
        }
    }

    private sealed class Host : Component
    {
        public readonly Consumer Consumer = new();
        public string LastHtml = "";

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => Consumer);
            ctx.NotifyParameters(c, false);
            return Div[c];
        }
    }

    // Owns the state and hands an async delegate to a CHILD component — the shape AutoCallback exists for,
    // and the shape a data grid's OnSortChangeAsync has. Its props are stable, so it is served from the
    // render cache unless something marks it dirty.
    private sealed class Consumer : Component
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _state = "idle";

        protected override Component? Render() =>
            Div[
                new Child { OnActAsync = ActAsync },
                Span["state: ", _state]
            ];

        // Deliberately no StateHasChanged: the framework owns both renders.
        private async Task ActAsync()
        {
            _state = "busy";
            Started.SetResult();
            await Release.Task;
            _state = "done";
        }
    }

    // Renders the element that carries the DOM handler, so the handler's own owner is this child — never the
    // consumer whose state the callback mutates. Without AutoCallback marking the consumer dirty up front,
    // the mid-await render would serve it from cache and "state: busy" would never appear.
    private sealed class Child : Component
    {
        public Func<Task>? OnActAsync { get; set; }

        protected override Component? Render() =>
            Button.OnClick(() => AutoCallback.Wrap(OnActAsync)!())["go"];
    }
}
