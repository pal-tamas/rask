using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated chain entries

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>
///     Pages whose <c>OnMount</c> reads something that throws — before the hook's first await, or after it
///     when the test holds the read — in an app that wrote no boundary around them.
/// </summary>
internal sealed partial class HookFaultStubApp : Component
{
    private static readonly Route[] Pages =
    [
        Route.To<HookFaultStubHome>("/"), Route.To<HookFaultStubPage>("/mount"), Route.To<HookFaultStubDeep>("/deep"),
    ];

    private static TaskCompletionSource? _held;

    private static int _started;

    /// <summary>Holds the next read until <see cref="Release" />, so it throws after an await.</summary>
    internal static void Hold()
    {
        Volatile.Write(ref _started, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    /// <summary>Completes once a held read has started.</summary>
    internal static Task ReadStarted() =>
        Rask.TestSupport.WaitFor.True(static () => Volatile.Read(ref _started) > 0, "the held read to start");

    internal static async Task<string> Read()
    {
        if (_held is { } held)
        {
            Interlocked.Increment(ref _started);
            await held.Task.ConfigureAwait(false);
        }

        throw new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'.");
    }

    protected override Component? HeadAssets => Title["hook fault"];

    protected override string? HtmlLang => null;

    protected override Component? Render() => Router.Routes(Pages);
}

[SkipFactory]
internal sealed class HookFaultStubHome : Component
{
    protected override Component? Render() => P["home"];
}

[SkipFactory]
internal sealed class HookFaultStubPage : Component
{
    private string? _row;

    protected override async Task OnMount() => _row = await HookFaultStubApp.Read();

    protected override Component? Render() => P[_row ?? "loading"];
}

// The hook that faults belongs to a child two elements below the page.
[SkipFactory]
internal sealed class HookFaultStubDeep : Component
{
    protected override Component? Render() => Div[Section[new HookFaultStubPage()]];
}
