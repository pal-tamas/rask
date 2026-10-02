using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core.ScopedAssets;
using Rask.Web.Types;
using Lock = Rask.Web.Types.Lock;

namespace Rask.Web.Tests;

// A handler the browser awaits (a lock request's) that belongs to no component — a hosted service's — lives exactly as
// long as the call that runs it, and a test's fake can run it in the browser's place.
public sealed class WebAwaitedHandlerTests
{
    [Fact]
    public async Task A_handler_no_component_owns_runs_while_the_call_that_hands_it_over_is_in_flight()
    {
        var browser = new LockingBrowser("""[{"name":"sync","mode":"exclusive"}]""");
        string? held = null;

        using (FakeBrowser.Enter(browser))
        {
            await Navigator.Locks.Request("sync", lk =>
            {
                held = lk?.Name;
                return Task.CompletedTask;
            });
        }

        Assert.Equal("sync", held);
    }

    [Fact]
    public async Task A_handler_no_component_owns_is_let_go_once_its_call_has_settled()
    {
        var browser = new LockingBrowser("""[{"name":"sync","mode":"exclusive"}]""");
        var runs = 0;
        using (FakeBrowser.Enter(browser))
        {
            await Navigator.Locks.Request("sync", _ =>
            {
                runs++;
                return Task.CompletedTask;
            });
        }

        await ScopedScript.Invoke(browser.Handed!.Id, JsonDocument.Parse("[null]").RootElement);

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task A_fake_calls_back_the_awaited_handler_and_the_call_settles_after_it()
    {
        using var locks = Navigator.Locks.Fake();
        var release = new TaskCompletionSource();
        locks.CallsBack<Lock?>("request", (call, handler) => handler(new Lock { Name = (string)call.Args[0]!, Mode = LockMode.Exclusive }));
        string? held = null;

        var request = Navigator.Locks.Request("sync", new LockOptions { IfAvailable = true }, async lk =>
        {
            held = lk?.Name;
            await release.Task;
        }).AsTask();
        var settledWhileHeld = request.IsCompleted;
        release.SetResult();
        await request;

        Assert.Equal(("sync", false), (held, settledWhileHeld));
        Assert.Equal("request", locks.Calls.Single().Member);
    }

    [Fact]
    public async Task A_fake_answers_IsSupported_as_the_test_set_it_up()
    {
        using var locks = Navigator.Locks.Fake();
        locks.Returns(l => l.IsSupported, false);

        var supported = await Navigator.Locks.IsSupported;

        Assert.False(supported);
    }

    [Fact]
    public async Task A_fake_is_supported_unless_the_test_says_otherwise()
    {
        using var locks = Navigator.Locks.Fake();

        var supported = await Navigator.Locks.IsSupported;

        Assert.True(supported);
    }

    // The browser's side of a lock request: it calls the handler it was handed with the lock, and settles the request
    // only once the handler's promise has.
    private sealed class LockingBrowser(string lockJson) : IJSRuntime
    {
        public ScopedScript.ScriptCallback? Handed { get; private set; }

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Handed = args?.OfType<ScopedScript.ScriptCallback>().Single();
            await ScopedScript.Invoke(Handed!.Id, JsonDocument.Parse(lockJson).RootElement);
            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
