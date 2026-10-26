using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     A lifecycle hook is part of the event that started it: what it resolves with <c>Current.Service&lt;T&gt;()</c>
///     — itself or through a helper that takes nothing — stays that event's, alive until the hook is done, though the
///     navigation went out long before and other events have come and gone since.
/// </summary>
public class HookEventScopeTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_hook_that_outlives_its_navigation_reads_through_its_event_s_scope_until_it_is_done(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/countries");
        await page.Gates.Reached("countries", 1);
        page.Gates.Open("countries", 1);
        await page.Gates.Reached("countries", 2);
        await page.Click("nothing");
        page.Gates.Open("countries", 2);
        await page.Gates.Done("countries");

        var read = page.Gates.Read("countries");
        Assert.Null(read.Failure);
        await page.Shows("countries row");
        Assert.Same(read.First, read.Again);
        await WaitFor.True(() => read.First!.Disposed, "the hook's scope to be disposed once the hook is done");
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Two_navigations_in_a_row_each_keep_their_own_scope_while_their_hooks_run(LiveTransportKind transport)
    {
        await using var page = await OpenAsync(transport);

        await page.Navigate("/hook-scope/countries");
        await page.Gates.Reached("countries", 1);
        await page.Navigate("/hook-scope/refunds");
        await page.Gates.Reached("refunds", 1);
        page.Gates.Open("countries", 1);
        page.Gates.Open("countries", 2);
        await page.Gates.Done("countries");
        page.Gates.Open("refunds", 1);
        page.Gates.Open("refunds", 2);
        await page.Gates.Done("refunds");

        var countries = page.Gates.Read("countries");
        var refunds = page.Gates.Read("refunds");
        Assert.Null(countries.Failure);
        Assert.Null(refunds.Failure);
        await page.Shows("refunds row");
        Assert.Same(countries.First, countries.Again);
        Assert.Same(refunds.First, refunds.Again);
        Assert.NotSame(countries.First, refunds.First);
    }

    [Fact]
    public async Task A_hook_that_never_ends_lets_its_scope_go_once_its_component_has_left_the_page_and_the_grace_is_over()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);
        ((Rask.Core.Live.EventServices)page.Session.EventServices).TeardownGrace = TimeSpan.Zero;

        await page.Navigate("/hook-scope/countries");
        await page.Gates.Reached("countries", 1);
        var held = page.Gates.Read("countries").First!;
        var heldWhileOnThePage = held.Disposed;
        await page.Navigate("/hook-scope");

        Assert.False(heldWhileOnThePage);
        await WaitFor.True(() => held.Disposed, "the scope of a hook whose component has gone to be disposed");
    }

    [Fact]
    public async Task The_session_ending_disposes_the_scope_a_hook_still_holds()
    {
        await using var page = await OpenAsync(LiveTransportKind.WebSocket);

        await page.Navigate("/hook-scope/countries");
        await page.Gates.Reached("countries", 1);
        var held = page.Gates.Read("countries").First!;
        await page.Session.DisposeAsync();

        Assert.True(held.Disposed);
        page.Gates.Open("countries", 1);
        page.Gates.Open("countries", 2);
    }

    private static async Task<OpenPage> OpenAsync(LiveTransportKind transport)
    {
        var host = RaskTestHost.Create<HookScopeApp>(
            services =>
            {
                services.AddSingleton<HookScopeGates>();
                services.AddScoped<HookScopeDb>();
            });
        var html = await host.Http.GetStringAsync("/hook-scope", TestContext.Current.CancellationToken);
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        var settled = session.AttachesSettled;
        var connection = await LiveTestConnection.OpenAsync(host, transport, session.Id);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;
        return new OpenPage(host, connection, session, html);
    }

    private sealed class OpenPage(RaskTestHost host, ILiveTestConnection connection, LiveSession session, string html)
        : IAsyncDisposable
    {
        public LiveSession Session => session;

        public HookScopeGates Gates => host.Server.Services.GetRequiredService<HookScopeGates>();

        public Task Navigate(string path) => connection.SendJsonAsync(new { type = "navigate", path, query = "" });

        public async Task Click(string button)
        {
            var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
            await connection.SendJsonAsync(new { id });
            await connection.SettledAsync();
        }

        public Task Shows(string text) =>
            connection.ReceiveUntilAsync(frame => frame.Contains(text, StringComparison.Ordinal), $"a frame showing {text}");

        public async ValueTask DisposeAsync()
        {
            Gates.OpenAll();
            await connection.DisposeAsync();
            await host.StopAsync();
            host.Dispose();
        }
    }
}

/// <summary>What a page's hook reads, held at two points the test opens: before and after it resolves again.</summary>
public sealed class HookScopeGates
{
    private readonly ConcurrentDictionary<(string Page, int Gate), TaskCompletionSource> _gates = new();
    private readonly ConcurrentDictionary<(string Page, int Gate), int> _reached = new();
    private readonly ConcurrentDictionary<string, HookScopeRead> _reads = new();

    public HookScopeRead Read(string page) => _reads.GetOrAdd(page, static _ => new HookScopeRead());

    public Task Reached(string page, int gate) =>
        WaitFor.True(() => _reached.ContainsKey((page, gate)), $"{page}'s hook to reach gate {gate}");

    public Task Done(string page) => WaitFor.True(() => Read(page).Done, $"{page}'s hook to finish");

    public void Open(string page, int gate) => Gate(page, gate).TrySetResult();

    public void OpenAll()
    {
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }
    }

    public Task Pass(string page, int gate)
    {
        _reached[(page, gate)] = 1;
        return Gate(page, gate).Task;
    }

    private TaskCompletionSource Gate(string page, int gate) =>
        _gates.GetOrAdd((page, gate), static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public sealed class HookScopeRead
{
    public HookScopeDb? First { get; set; }

    public HookScopeDb? Again { get; set; }

    public Exception? Failure { get; set; }

    public bool Done { get; set; }
}

// The app's DbContext: unusable once its scope is disposed.
public sealed class HookScopeDb : IDisposable
{
    public bool Disposed { get; private set; }

    public string[] Rows(string page)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        return [page + " row"];
    }

    public void Dispose() => Disposed = true;
}

// A helper with no constructor, as an app's catalogue is: it reaches the event's services by itself.
public static class HookScopeCatalogue
{
    public static async Task<string[]> Load(string page)
    {
        var gates = Current.Service<HookScopeGates>();
        var read = gates.Read(page);
        try
        {
            read.First = Current.Service<HookScopeDb>();
            await gates.Pass(page, 1);
            read.Again = Current.Service<HookScopeDb>();
            await gates.Pass(page, 2);
            return [.. read.First.Rows(page), .. read.Again.Rows(page)];
        }
        catch (Exception ex)
        {
            read.Failure = ex;
            return [];
        }
        finally
        {
            read.Done = true;
        }
    }
}

public sealed partial class HookScopeApp : Component
{
    protected override Component? HeadAssets => Title["hook scope"];

    protected override Component? Render() =>
        Div[
            Button.OnClick(static () => { })["nothing"],
            Router
        ];
}

[Route("/hook-scope")]
public sealed partial class HookScopeHome : Component
{
    protected override Component? Render() => P["home"];
}

[Route("/hook-scope/countries")]
public sealed partial class HookScopeCountries : Component
{
    private string[] _rows = [];

    protected override async Task OnUpdated() => _rows = await HookScopeCatalogue.Load("countries");

    protected override Component? Render() => P[string.Join(", ", _rows)];
}

[Route("/hook-scope/refunds")]
public sealed partial class HookScopeRefunds : Component
{
    private string[] _rows = [];

    protected override async Task OnUpdated() => _rows = await HookScopeCatalogue.Load("refunds");

    protected override Component? Render() => P[string.Join(", ", _rows)];
}
