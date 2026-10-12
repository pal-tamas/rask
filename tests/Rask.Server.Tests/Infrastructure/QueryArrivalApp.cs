using Rask.Core;
using Rask.Core.Routing;
using Rask.Querying;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A list that reads through <c>Rask.Query</c> where it renders — a spinner while there is nothing to show,
///     the rows once there is — and a page whose buttons go to it, one of them after a save that makes the
///     list's query out of date.
/// </summary>
public sealed partial class QueryArrivalApp : Component
{
    protected override Component? HeadAssets => Markup.Title["query arrival"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>What the list's query reads. A read is held until the test lets it go.</summary>
public sealed class QueryArrivalStore
{
    private TaskCompletionSource? _held;
    private int _heldReads;

    /// <summary>How many reads have started.</summary>
    public int Reads;

    /// <summary>What a save bumps: the rows say which version they were read at.</summary>
    public int Version = 1;

    public void Hold()
    {
        Volatile.Write(ref _heldReads, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    /// <summary>Completes once a read has started since the last <see cref="Hold" />.</summary>
    public Task ReadStarted() => Rask.TestSupport.WaitFor.True(() => Volatile.Read(ref _heldReads) > 0, "a held read to start");

    public async Task<string[]> Rows()
    {
        Interlocked.Increment(ref Reads);
        if (_held is { } held)
        {
            Interlocked.Increment(ref _heldReads);
            await held.Task.ConfigureAwait(false);
        }

        var version = Volatile.Read(ref Version);
        return [.. Enumerable.Range(1, 3).Select(n => $"order v{version}-{n}")];
    }

    public Task Save()
    {
        Interlocked.Increment(ref Version);
        return Task.Delay(1);
    }
}

[Route("/q/home")]
public sealed partial class QueryArrivalHome(QueryArrivalStore store, IQueryClient queries) : Component
{
    protected override Component? Render() =>
    [
        P.Id("home")["home"],
        Button.Id("go").OnClick(static () => Routes.QueryArrivalList().Go())["to the list"],
        Button.Id("save").OnClick(SaveThenGo)["save"],
    ];

    // The documented save: the write, what it makes out of date, and the list.
    private async Task SaveThenGo()
    {
        await store.Save();
        queries.Invalidate(QueryArrivalList.Orders);
        Routes.QueryArrivalList().Go();
    }
}

[Route("/q/list")]
public sealed partial class QueryArrivalList : Component
{
    internal const string Orders = "q-orders";

    private readonly Query<string[]> _orders;

    public QueryArrivalList(QueryArrivalStore store, IQueryClient queries) =>
        _orders = queries.Query<string[]>(Orders, _ => store.Rows(), new QueryOptions { StaleTime = TimeSpan.FromHours(1) });

    protected override Task OnUnmount()
    {
        _orders.Dispose();
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        _orders.IsLoading
            ? P.Id("spinner")["spinner"]
            : Ul.Id("orders")[(_orders.Data ?? []).Select(static row => (Component)Li.Key(row)[row]).ToArray()];
}
