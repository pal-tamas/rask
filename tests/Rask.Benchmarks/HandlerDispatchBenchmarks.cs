using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rask.Core;

#pragma warning disable RASK014 // benchmark-local Component subclass has no generated factory

namespace Rask.Benchmarks;

/// <summary>
///     An event reaching its C# handler: <c>Component.TryInvokeHandlerAsync</c>, the entry every WebSocket, HTTP and
///     WebAssembly dispatch ends in.
/// </summary>
/// <remarks>
///     <para>
///         <c>WsDispatchBenchmarks</c> and <c>WasmDispatchBenchmarks</c> only parse the inbound frame; neither calls a
///         handler, so a change to dispatch read <c>+0 B</c> on both whatever it did (#1062). This drives the dispatch
///         itself: the tree is rendered once so the handler map exists, then each benchmark invokes one handler through
///         the same entry a live session uses, with a payload parsed up front so only dispatch is measured.
///     </para>
///     <para>
///         The render a handler requests is not part of this — with no session attached, StateHasChanged only marks
///         the component dirty. <c>LiveSessionSendBenchmarks</c> measures the state change → render → diff → send
///         path that follows.
///     </para>
/// </remarks>
[MemoryDiagnoser]
public partial class HandlerDispatchBenchmarks : global::Rask.Core.RaskMarkup
{
    private static readonly JsonElement ClickPayload = JsonDocument.Parse("""{"id":"h0","type":"click"}""").RootElement.Clone();

    private DispatchHost _host = null!;
    private string _syncId = null!;
    private string _asyncId = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _host = new DispatchHost();
        var html = _host.RenderAsLiveRoot();

        // Read off the markup, as the browser reads them: the sync button renders first, then the async one.
        var ids = System.Text.RegularExpressions.Regex.Matches(html, "data-rask-on-click=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();
        if (ids.Length != 2)
        {
            throw new InvalidOperationException($"expected two click handlers in the rendered host, found {ids.Length}: {html}");
        }

        _syncId = ids[0];
        _asyncId = ids[1];
    }

    /// <summary>A synchronous <c>Action</c> handler — a counter's click.</summary>
    [Benchmark(Baseline = true)]
    public bool SyncHandler() => _host.TryInvokeHandlerAsync(_syncId, ClickPayload).GetAwaiter().GetResult();

    /// <summary>An asynchronous handler that completes synchronously — the common save-then-return shape.</summary>
    [Benchmark]
    public bool AsyncHandler() => _host.TryInvokeHandlerAsync(_asyncId, ClickPayload).GetAwaiter().GetResult();

    /// <summary>An id with no handler behind it — a stale event for an element that has since gone.</summary>
    [Benchmark]
    public bool UnknownHandler() => _host.TryInvokeHandlerAsync("h-unknown", ClickPayload).GetAwaiter().GetResult();

    private sealed class DispatchHost : Component
    {
        private int _clicks;

        protected override Component? Render() =>
            Div[
                Button.OnClick(() => _clicks++)["sync"],
                Button.OnClick(() =>
                {
                    _clicks++;
                    return Task.CompletedTask;
                })["async"],
                Span[_clicks.ToString(System.Globalization.CultureInfo.InvariantCulture)]
            ];
    }
}
