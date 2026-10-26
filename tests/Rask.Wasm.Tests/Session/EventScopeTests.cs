using System.Collections.Concurrent;
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

    [Fact]
    public async Task A_read_a_hook_detaches_keeps_its_event_s_scope_until_it_is_back_on_the_page()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());
        var gate = services.GetRequiredService<WasmDetachedGate>();

        await Click(session, html, "detach");
        await WaitFor.True(() => gate.Probe is not null, "the detached read to take its service");
        await Click(session, html, "use");
        var whileReading = gate.Probe!.Disposed;
        gate.Read.SetResult();
        await WaitFor.True(() => gate.Back, "the detached read to come back to the page");

        Assert.False(whileReading);
        await WaitFor.True(() => gate.Probe.Disposed, "the scope to go once the read is back");
    }

    // As a Livewire component's action is a request of its own: three components loading at once never share one.
    [Fact]
    public async Task Three_components_loading_at_once_in_one_event_each_read_through_their_own_scope()
    {
        var (session, services) = NewScopedSession();
        var html = Html(await session.InitialRenderAsync());
        var loads = services.GetRequiredService<WasmLoads>();

        await Click(session, html, "three");
        await WaitFor.True(() => loads.Reached == 3 || loads.Failure is not null, "every component's hook to hold its read");
        loads.Gate.SetResult();
        await WaitFor.True(() => loads.Done == 3, "every component's hook to finish");

        var reads = loads.Reads.ToArray();
        Assert.Null(loads.Failure);
        Assert.All(reads, static read => Assert.Same(read.First, read.Again));
        Assert.Equal(3, reads.Select(static read => read.First).Distinct().Count());
        await WaitFor.True(() => Array.TrueForAll(reads, static read => read.First.Disposed), "each component's scope to go once its hook is done");
    }

    private static (WasmLiveSession Session, IServiceProvider Services) NewScopedSession() =>
        NewSession<WasmScopeApp>(configure: s =>
        {
            EventScopeCatalog.Add(s);
            s.AddSingleton<WasmScopeLog>();
            s.AddScoped<WasmScopeProbe>();
            s.AddSingleton<WasmHookGate>();
            s.AddSingleton<WasmDetachedGate>();
            s.AddSingleton<WasmLoads>();
            s.AddScoped<WasmBusyDb>();
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

// What a detached read waits on, held by the test.
public sealed class WasmDetachedGate
{
    public TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WasmScopeProbe? Probe { get; set; }

    public bool Back { get; set; }
}

// The hook goes out at once and its read follows: it takes the provider and reads through it on a task of its own.
public sealed partial class WasmDetachedChild : Component
{
    protected override async Task OnMount()
    {
        await Task.Yield();

        // Detached on purpose: the read comes back to the page by itself.
        _ = Load();
    }

    protected override Component? Render() => P["detached"];

    private static async Task Load()
    {
        var services = Current.Service<IServiceProvider>();
        var gate = services.GetRequiredService<WasmDetachedGate>();
        await Task.Run(async () =>
        {
            gate.Probe = services.GetRequiredService<WasmScopeProbe>();
            await gate.Read.Task;
            gate.Probe.Use();
        });
        gate.Back = true;
    }
}

// What three loaders read, held by the test at one gate.
public sealed class WasmLoads
{
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _reached;
    private int _done;

    public ConcurrentQueue<(WasmBusyDb First, WasmBusyDb Again)> Reads { get; } = new();

    public int Reached => Volatile.Read(ref _reached);

    public int Done => Volatile.Read(ref _done);

    public Exception? Failure { get; set; }

    public void Reach() => Interlocked.Increment(ref _reached);

    public void Finish() => Interlocked.Increment(ref _done);
}

// A DbContext as EF Core has it: one operation at a time.
public sealed class WasmBusyDb : IDisposable
{
    private int _busy;

    public bool Disposed { get; private set; }

    public void BeginRead()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw new InvalidOperationException("A second operation was started on this context instance before a previous operation completed.");
        }
    }

    public void EndRead() => Volatile.Write(ref _busy, 0);

    public void Dispose() => Disposed = true;
}

public sealed partial class WasmLoader : Component
{
    protected override async Task OnMount()
    {
        var loads = Current.Service<WasmLoads>();
        try
        {
            var db = Current.Service<WasmBusyDb>();
            db.BeginRead();
            loads.Reach();
            await loads.Gate.Task;
            db.EndRead();
            loads.Reads.Enqueue((db, Current.Service<WasmBusyDb>()));
        }
        catch (Exception ex)
        {
            loads.Failure = ex;
        }
        finally
        {
            loads.Finish();
        }
    }

    protected override Component? Render() => P["loader"];
}

public sealed partial class WasmScopeApp : Component
{
    private bool _show;
    private bool _detach;
    private bool _three;

    protected override Component? Render() =>
        Div[
            Button.Id("show").OnClick(() => _show = true)["show"],
            _show ? WasmHookChild : null,
            Button.Id("detach").OnClick(() => _detach = true)["detach"],
            _detach ? WasmDetachedChild : null,
            Button.Id("three").OnClick(() => _three = true)["three"],
            _three ? Div[WasmLoader, WasmLoader, WasmLoader] : null,
            Button.Id("use").OnClick(static () => Ambient.Services!.GetRequiredService<WasmScopeProbe>().Use())["use"],
            Button.Id("current").OnClick(static async () =>
            {
                Current.Service<WasmScopeProbe>().Use();
                await Task.Yield();
                Current.Service<WasmScopeProbe>().Use();
            })["current"]
        ];
}
