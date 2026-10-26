using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     As a Livewire component's action is a request of its own, each component's part of a live event gets the app's
///     scoped services of its own: three components loading at once on one page never share a <c>DbContext</c>.
/// </summary>
public class ComponentEventScopeTests
{
    private static readonly string[] Tables = ["notices", "progress", "files"];

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_first_request_whose_three_components_load_at_once_renders_all_three(LiveTransportKind transport)
    {
        var host = Host();
        var loads = host.Server.Services.GetRequiredService<ScopeLoads>();

        var answer = host.Http.GetStringAsync("/component-scope/files", TestContext.Current.CancellationToken);
        await loads.AllReached(Tables);
        loads.OpenAll();
        var html = await answer;
        await using var page = await AttachAsync(host, transport, html);

        loads.AssertEachReadThroughItsOwnScope(Tables);
        Assert.All(Tables, table => Assert.Contains(table + " row", html, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_navigation_whose_three_components_load_at_once_shows_all_three(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/component-scope/files");
        await page.Loads.AllReached(Tables);
        foreach (var table in Tables)
        {
            page.Loads.Open(table);
            await page.Shows(table + " row");
        }

        page.Loads.AssertEachReadThroughItsOwnScope(Tables);
    }

    [Fact]
    public async Task Each_component_s_scope_is_disposed_once_its_own_hook_is_done()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);

        await page.Navigate("/component-scope/files");
        await page.Loads.AllReached(Tables);
        page.Loads.Open("notices");
        page.Loads.Open("progress");
        await WaitFor.True(() => page.Loads.For("notices").First!.Disposed && page.Loads.For("progress").First!.Disposed,
            "the scopes of the two components whose hooks are done to be disposed");
        var filesWhileReading = page.Loads.For("files").First!.Disposed;
        page.Loads.Open("files");

        Assert.False(filesWhileReading);
        await WaitFor.True(() => page.Loads.For("files").First!.Disposed, "the last component's scope to go once its hook is done");
    }

    private static RaskTestHost Host() =>
        RaskTestHost.Create<ComponentScopeApp>(services =>
        {
            services.AddSingleton<ScopeLoads>();
            services.AddScoped<ScopeDb>();
        });

    private static async Task<OpenPage> OpenAsync(LiveTransportKind transport)
    {
        var host = Host();
        var html = await host.Http.GetStringAsync("/component-scope", TestContext.Current.CancellationToken);
        return await AttachAsync(host, transport, html);
    }

    private static async Task<OpenPage> AttachAsync(RaskTestHost host, LiveTransportKind transport, string html)
    {
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        var settled = session.AttachesSettled;
        var connection = await LiveTestConnection.OpenAsync(host, transport, session.Id);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;
        return new OpenPage(host, connection);
    }

    private sealed class OpenPage(RaskTestHost host, ILiveTestConnection connection) : IAsyncDisposable
    {
        public ScopeLoads Loads => host.Server.Services.GetRequiredService<ScopeLoads>();

        public Task Navigate(string path) => connection.SendJsonAsync(new { type = "navigate", path, query = "" });

        public Task Shows(string text) =>
            connection.ReceiveUntilAsync(frame => frame.Contains(text, StringComparison.Ordinal), $"a frame showing {text}");

        public async ValueTask DisposeAsync()
        {
            Loads.OpenAll();
            await connection.DisposeAsync();
            await host.StopAsync();
            host.Dispose();
        }
    }
}

/// <summary>What each component's hook read, and the gate it holds its read at until the test opens it.</summary>
public sealed class ScopeLoads
{
    private readonly ConcurrentDictionary<string, ScopeLoad> _loads = new();

    public ScopeLoad For(string table) => _loads.GetOrAdd(table, static _ => new ScopeLoad());

    public Task AllReached(string[] tables) =>
        WaitFor.True(() => tables.All(t => For(t).Reached || For(t).Failure is not null), "every component's hook to hold its read");

    public void Open(string table) => For(table).Gate.TrySetResult();

    public void OpenAll()
    {
        foreach (var load in _loads.Values)
        {
            load.Gate.TrySetResult();
        }
    }

    public void AssertEachReadThroughItsOwnScope(string[] tables)
    {
        foreach (var table in tables)
        {
            var load = For(table);
            Assert.Null(load.Failure);
            Assert.Same(load.First, load.Again);
        }

        Assert.Equal(tables.Length, tables.Select(t => For(t).First).Distinct().Count());
    }
}

public sealed class ScopeLoad
{
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ScopeDb? First { get; set; }

    public ScopeDb? Again { get; set; }

    public Exception? Failure { get; set; }

    public bool Reached { get; set; }
}

// The app's DbContext as EF Core has it: one operation at a time, and unusable once its scope is disposed.
public sealed class ScopeDb : IDisposable
{
    private int _busy;

    public bool Disposed { get; private set; }

    public void BeginRead()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw new InvalidOperationException(
                "A second operation was started on this context instance before a previous operation completed.");
        }
    }

    public string EndRead(string table)
    {
        Volatile.Write(ref _busy, 0);
        return table + " row";
    }

    public void Dispose() => Disposed = true;
}

// A component that loads its table as an app's does: `var db = Current.Service<ApplicationDbContext>();` and a read.
public abstract partial class ScopeLoader : Component
{
    private string _row = "";

    protected abstract string Table { get; }

    protected override async Task OnUpdated()
    {
        var load = Current.Service<ScopeLoads>().For(Table);
        try
        {
            var db = Current.Service<ScopeDb>();
            load.First = db;
            db.BeginRead();
            load.Reached = true;
            await load.Gate.Task;
            _row = db.EndRead(Table);
            load.Again = Current.Service<ScopeDb>();
        }
        catch (Exception ex)
        {
            load.Failure = ex;
        }
    }

    protected override Component? Render() => P[_row];
}

public sealed partial class ScopeNotices : ScopeLoader
{
    protected override string Table => "notices";
}

public sealed partial class ScopeProgress : ScopeLoader
{
    protected override string Table => "progress";
}

public sealed partial class ScopeFiles : ScopeLoader
{
    protected override string Table => "files";
}

public sealed partial class ComponentScopeApp : Component
{
    protected override Component? HeadAssets => Title["component scope"];

    protected override Component? Render() => Div[Router];
}

[Route("/component-scope")]
public sealed partial class ComponentScopeHome : Component
{
    protected override Component? Render() => P["home"];
}

[Route("/component-scope/files")]
public sealed partial class ComponentScopeFilesPage : Component
{
    protected override Component? Render() => Div[ScopeNotices, ScopeProgress, ScopeFiles];
}
