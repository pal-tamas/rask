using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// In the browser as on a server: each event gets the app's scoped services fresh, and they go when it has rendered.
[Collection("WasmSession")]
public class EventScopeTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task Two_clicks_get_two_different_instances_of_a_scoped_service()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());

        await Click(session, html, "use");
        await Click(session, html, "use");

        var seen = services.GetRequiredService<WasmScopeLog>().Seen;
        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
    }

    [Fact]
    public async Task The_event_s_scope_is_disposed_once_the_event_has_rendered()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());

        await Click(session, html, "use");

        await WaitFor.True(() => services.GetRequiredService<WasmScopeLog>().Seen[0].Disposed, "the event's scope to be disposed");
        Assert.Null(session.OpenedEventScope);
    }

    [Fact]
    public async Task Current_Service_hands_a_handler_its_event_s_instance_across_an_await()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());

        await Click(session, html, "current");
        await Click(session, html, "current");

        var seen = services.GetRequiredService<WasmScopeLog>().Seen;
        Assert.Equal(4, seen.Count);
        Assert.Same(seen[0], seen[1]);
        Assert.NotSame(seen[1], seen[2]);
        await WaitFor.True(() => seen.TrueForAll(static probe => probe.Disposed), "each event's scope to be disposed");
    }

    [Fact]
    public async Task A_hook_keeps_its_event_s_scope_until_it_is_done_though_other_events_end_meanwhile()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());
        var gate = services.GetRequiredService<WasmHookGate>();

        await Click(session, html, "show");
        await WaitFor.True(() => gate.Reached >= 1, "the hook to hold its read");
        await Click(session, html, "use");
        gate.First.SetResult();
        await WaitFor.True(() => gate.Reached >= 2, "the hook to resolve again after its event");
        await Click(session, html, "use");
        var whileReading = gate.Seen.Select(static probe => probe.Disposed).ToArray();
        gate.Second.SetResult();
        await WaitFor.True(() => gate.Done, "the hook to finish");

        Assert.Equal([false, false], whileReading);
        Assert.Same(gate.Seen[0], gate.Seen[1]);
        await WaitFor.True(() => gate.Seen[0].Disposed, "the hook's scope to go once it is done");
    }

    private static (WasmLiveSession Session, IServiceProvider Services) NewScopedSession() =>
        NewSession<WasmScopeApp>(configure: s =>
        {
            EventScopeCatalog.Add(s);
            s.AddSingleton<WasmScopeLog>();
            s.AddScoped<WasmScopeProbe>();
            s.AddSingleton<WasmHookGate>();
        });

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static async Task Click(WasmLiveSession session, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handler}}","type":"click"}"""));
    }
}

public sealed class WasmScopeLog
{
    public List<WasmScopeProbe> Seen { get; } = [];
}

public sealed class WasmScopeProbe(WasmScopeLog log) : IDisposable
{
    public bool Disposed { get; private set; }

    public void Use() => log.Seen.Add(this);

    public void Dispose() => Disposed = true;
}

// What a hook's read waits on, held by the test.
public sealed class WasmHookGate
{
    public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<WasmScopeProbe> Seen { get; } = [];

    public int Reached => Seen.Count;

    public bool Done { get; set; }
}

public sealed partial class WasmHookChild : Component
{
    protected override async Task OnMount()
    {
        var gate = Current.Service<WasmHookGate>();
        gate.Seen.Add(Current.Service<WasmScopeProbe>());
        await gate.First.Task;
        gate.Seen.Add(Current.Service<WasmScopeProbe>());
        await gate.Second.Task;
        gate.Done = true;
    }

    protected override Component? Render() => P["child"];
}

public sealed partial class WasmScopeApp : Component
{
    private bool _show;

    protected override Component? Render() =>
        Div[
            Button.Id("show").OnClick(() => _show = true)["show"],
            _show ? WasmHookChild : null,
            Button.Id("use").OnClick(static () => Ambient.Services!.GetRequiredService<WasmScopeProbe>().Use())["use"],
            Button.Id("current").OnClick(static async () =>
            {
                Current.Service<WasmScopeProbe>().Use();
                await Task.Yield();
                Current.Service<WasmScopeProbe>().Use();
            })["current"]
        ];
}
