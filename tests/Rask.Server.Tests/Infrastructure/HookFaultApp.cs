using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     Pages whose lifecycle hooks read something that throws: before the hook's first await when the store is
///     not held, after it when it is. None of them is wrapped in a boundary of the app's, but one.
/// </summary>
public sealed partial class HookFaultApp : Component
{
    protected override Component? HeadAssets => Title["hook fault"];

    protected override Component? Render() => Router;
}

/// <summary>What the hooks read. A read is held until the test lets it go, and then it throws.</summary>
public sealed class HookFaultStore
{
    public const string Secret = "Password=hunter2";

    private TaskCompletionSource? _held;
    private int _heldReads;

    public void Hold()
    {
        Volatile.Write(ref _heldReads, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    /// <summary>Completes once a read has started since the last <see cref="Hold" />.</summary>
    public Task ReadStarted() => Rask.TestSupport.WaitFor.True(() => Volatile.Read(ref _heldReads) > 0, "a held read to start");

    public async Task<string> Read()
    {
        if (_held is { } held)
        {
            Interlocked.Increment(ref _heldReads);
            await held.Task.ConfigureAwait(false);
        }

        return Mended ? "the row" : throw new HookFaultException(Secret);
    }

    /// <summary>Whether what was wrong has been put right: a read answers from then on.</summary>
    public bool Mended { get; set; }
}

public sealed class HookFaultException(string message) : Exception(message);

[Route("/hook/home")]
public sealed partial class HookFaultHome : Component
{
    protected override Component? Render() => P.Id("home")["home"];
}

[Route("/hook/mount")]
public sealed partial class HookFaultOnMount(HookFaultStore store) : Component
{
    private string? _row;

    protected override async Task OnMount() => _row = await store.Read();

    protected override Component? Render() => P.Id("page")[_row ?? "loading"];
}

[Route("/hook/updated")]
public sealed partial class HookFaultOnUpdated(HookFaultStore store) : Component
{
    private string? _row;

    protected override async Task OnUpdated() => _row = await store.Read();

    protected override Component? Render() => P.Id("page")[_row ?? "loading"];
}

[Route("/hook/rendered")]
public sealed partial class HookFaultOnRendered(HookFaultStore store) : Component
{
    private string? _row;

    protected override async Task OnRendered() => _row ??= await store.Read();

    protected override Component? Render() => P.Id("page")[_row ?? "loading"];
}

// The hook that faults is three levels below the page.
[Route("/hook/deep")]
public sealed partial class HookFaultDeep : Component
{
    protected override Component? Render() => Div.Id("deep")[Section[HookFaultPanel]];
}

public sealed partial class HookFaultPanel : Component
{
    protected override Component? Render() => Article[HookFaultRow];
}

public sealed partial class HookFaultRow(HookFaultStore store) : Component
{
    private string? _row;

    protected override async Task OnMount() => _row = await store.Read();

    protected override Component? Render() => P.Id("row")[_row ?? "loading"];
}

// The same row, inside a boundary the app wrote.
[Route("/hook/guarded")]
public sealed partial class HookFaultGuarded : Component
{
    protected override Component? Render() =>
        Div[
            P.Id("beside")["beside"],
            ErrorBoundary.Fallback(static (_, _) => P.Id("own")["could not load the row"])[HookFaultRow]];
}

[Route("/hook-layout")]
public sealed partial class HookFaultLayout(HookFaultStore store) : Component
{
    private string? _menu;

    protected override async Task OnMount() => _menu = await store.Read();

    protected override Component? Render() => Div[Nav.Id("menu")[_menu ?? "loading"], Outlet];
}

[Route("page")]
[ParentRoute(typeof(HookFaultLayout))]
public sealed partial class HookFaultLayoutPage : Component
{
    protected override Component? Render() => P.Id("inside")["inside the layout"];
}
