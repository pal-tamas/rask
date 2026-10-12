using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK014 // routed through an explicit table: these pages have no generated entries
#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

// A list whose read the test holds: what the browser host sends of a page reached in place while its
// OnUpdated is still waiting. The session tests run one at a time, so the gate is the app's own.
internal sealed partial class HeldListStubApp : Component
{
    private static readonly Route[] Pages = [Route.To<HeldListStubHome>("/"), Route.To<HeldListStubList>("/list")];

    private static TaskCompletionSource _read = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int _started;

    /// <summary>The read every list waits on.</summary>
    internal static Task Read
    {
        get
        {
            Interlocked.Increment(ref _started);
            return _read.Task;
        }
    }

    /// <summary>Completes once a list has started its read since the last <see cref="Hold" />.</summary>
    internal static Task ReadStarted() =>
        Rask.TestSupport.WaitFor.True(static () => Volatile.Read(ref _started) > 0, "the list to start its read");

    /// <summary>Holds the next list's read until <see cref="Release" />.</summary>
    internal static void Hold()
    {
        Volatile.Write(ref _started, 0);
        _read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static void Release() => _read.TrySetResult();

    protected override Component? HeadAssets => Title["held"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => Router.Routes(Pages);
}

[SkipFactory]
internal sealed class HeldListStubHome : Component
{
    protected override Component? Render() => P["home"];
}

[SkipFactory]
internal sealed class HeldListStubList : Component
{
    private int? _rows;

    protected override async Task OnUpdated()
    {
        await HeldListStubApp.Read;
        _rows = 15;
    }

    protected override Component? Render() => Main[H1["list"], _rows is null ? null : Table[Tbody[Tr[Td[$"rows:{_rows}"]]]]];
}
