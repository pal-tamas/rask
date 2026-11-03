using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Querying;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     The shapes a page changes its rows in from a handler: the handler awaits the read itself, it says
///     "loading" first, a child reads again in <c>OnUpdated</c>, a query's key moves, the address changes — and a
///     save that says "saving" while it runs. Every read is the store's, held and released by the test.
/// </summary>
public sealed partial class HandlerWaitApp : Component
{
    protected override Component? HeadAssets => Markup.Title["handler wait"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>What the pages read. A read is held until the test lets it go.</summary>
public sealed class HandlerWaitStore
{
    private TaskCompletionSource? _held;
    private int _heldReads;

    // Each held read by what it reads for, so a test can let one go before another.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource> _byRead = new();

    /// <summary>How many reads have started.</summary>
    public int Reads;

    public void Hold()
    {
        Volatile.Write(ref _heldReads, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    /// <summary>Lets go of the held read for <paramref name="of" /> alone; the others stay held.</summary>
    public void Release(string of)
    {
        if (_byRead.TryRemove(of, out var read))
        {
            read.TrySetResult();
        }
    }

    /// <summary>Completes once a read has started since the last <see cref="Hold" />.</summary>
    public Task ReadStarted() => Rask.TestSupport.WaitFor.True(() => Volatile.Read(ref _heldReads) > 0, "a held read to start");

    /// <summary>Three rows that say which page, or which order, they were read for.</summary>
    public async Task<string[]> Rows(object of)
    {
        await Read(of.ToString()!).ConfigureAwait(false);
        return [.. Enumerable.Range(1, 3).Select(n => $"row {of}-{n}")];
    }

    public Task Save() => Read("save");

    private Task Read(string of)
    {
        Interlocked.Increment(ref Reads);
        if (_held is not { } held)
        {
            return Task.CompletedTask;
        }

        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _byRead[of] = read;
        // Discarded on purpose: it only passes the release of every read on to this one, and cannot fault.
        _ = held.Task.ContinueWith(static (_, r) => ((TaskCompletionSource)r!).TrySetResult(), read, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Interlocked.Increment(ref _heldReads);
        return read.Task;
    }
}

/// <summary>The rows every page here draws, under the pager that says which page they should be.</summary>
internal static class HandlerWaitMarkup
{
    public static Component Pager(int page) => Markup.P.Id("pager")[$"pager at {page}"];

    public static Component Rows(string[]? rows) =>
        rows is null
            ? Markup.P.Id("loading")["loading"]
            : Markup.Ul.Id("rows")[rows.Select(static row => (Component)Markup.Li.Key(row)[row]).ToArray()];
}

/// <summary>The plain shape: the handler moves the page and awaits the read, and the old rows stay until it is back.</summary>
[Route("/w/plain")]
public sealed partial class HandlerWaitPlainPage(HandlerWaitStore store) : Component
{
    private int _page = 1;
    private string[]? _rows;

    protected override async Task OnMount() => _rows = await store.Rows("p1");

    protected override Component? Render() =>
    [
        HandlerWaitMarkup.Pager(_page),
        Button.Id("next").OnClick(Next)["next"],
        HandlerWaitMarkup.Rows(_rows),
    ];

    private async Task Next()
    {
        _page++;
        _rows = await store.Rows("p" + _page);
    }
}

/// <summary>The same, by a page that empties its rows and says so before it reads.</summary>
[Route("/w/loading")]
public sealed partial class HandlerWaitLoadingPage(HandlerWaitStore store) : Component
{
    private int _page = 1;
    private string[]? _rows;

    protected override async Task OnMount() => _rows = await store.Rows("p1");

    protected override Component? Render() =>
    [
        HandlerWaitMarkup.Pager(_page),
        Button.Id("next").OnClick(Next)["next"],
        HandlerWaitMarkup.Rows(_rows),
    ];

    private async Task Next()
    {
        _page++;
        _rows = null;
        _rows = await store.Rows("p" + _page);
    }
}

/// <summary>A handler that only changes a field. The rows are a child's, which reads again when the field reaches it.</summary>
[Route("/w/child")]
public sealed partial class HandlerWaitChildPage : Component
{
    private int _page = 1;

    protected override Component? Render() =>
    [
        HandlerWaitMarkup.Pager(_page),
        Button.Id("next").OnClick(() => _page++)["next"],
        HandlerWaitRows.Page(_page),
    ];
}

/// <summary>The rows of one page, read in <c>OnUpdated</c>. The ones it has stay until the next are back.</summary>
public sealed partial class HandlerWaitRows(HandlerWaitStore store) : Component
{
    private string[]? _rows;

    public int? Page { get; set; }

    // Two pages asked for one after the other are two reads in flight, and they come back in either order:
    // the rows kept are the ones of the page still asked for, as any list that pages must see to.
    protected override async Task OnUpdated()
    {
        var asked = Page;
        var rows = await store.Rows("p" + asked);
        if (asked == Page)
        {
            _rows = rows;
        }
    }

    protected override Component? Render() => HandlerWaitMarkup.Rows(_rows);
}

/// <summary>A page that reads through a query whose key is its page number. A handler moves the number.</summary>
[Route("/w/query")]
public sealed partial class HandlerWaitQueryPage(HandlerWaitStore store, IQueryClient queries) : Component
{
    private int _page = 1;

    private Query<string[]> Rows => field ??= queries.Query<int, string[]>(
        QueryKey.Of("w-rows"), () => _page, (page, _) => store.Rows("p" + page), Options);

    private QueryOptions Options => new() { StaleTime = TimeSpan.FromHours(1), KeepPreviousData = Kept };

    /// <summary>Whether the rows of the page before stay while the next one's are read.</summary>
    [QueryParam]
    public bool Kept { get; set; }

    protected override Task OnUnmount()
    {
        Rows.Dispose();
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        HandlerWaitMarkup.Pager(_page),
        Button.Id("next").OnClick(() => _page++)["next"],
        HandlerWaitMarkup.Rows(Rows.Data),
    ];
}

/// <summary>A list whose order is in its address: the heading's handler changes the query, which is a navigation.</summary>
[Route("/w/sorted")]
public sealed partial class HandlerWaitSortedPage(HandlerWaitStore store) : Component
{
    private string[]? _rows;

    [QueryParam]
    public string? Sort { get; set; }

    protected override async Task OnUpdated() => _rows = await store.Rows(Sort ?? "unsorted");

    protected override Component? Render() =>
    [
        Button.Id("sort").OnClick(static () => Go.With("sort", "name"))["by name"],
        HandlerWaitMarkup.Rows(_rows),
    ];
}

/// <summary>A save that says it is saving while it runs, and a button beside it that only counts.</summary>
[Route("/w/save")]
public sealed partial class HandlerWaitSavePage(HandlerWaitStore store) : Component
{
    private bool _saving;
    private int _saved;
    private int _pings;

    protected override Component? Render() =>
    [
        Button.Id("save").OnClick(Save)[_saving ? "saving" : "save"],
        Button.Id("ping").OnClick(() => _pings++)["ping"],
        P.Id("saved")[$"saved {_saved} pings {_pings}"],
    ];

    private async Task Save()
    {
        _saving = true;
        await store.Save();
        _saving = false;
        _saved++;
    }
}

/// <summary>A field whose rule asks the store, which is how an async validator's "Checking..." comes to be shown.</summary>
[Route("/w/checked")]
public sealed partial class HandlerWaitCheckedPage : Component
{
    private readonly EditContext _ctx;
    private readonly Signup _model = new();

    public HandlerWaitCheckedPage(HandlerWaitStore store)
    {
        // No tail after the check: the indicator is there exactly while the check runs.
        _ctx = new EditContext(_model) { ValidatingStickyMs = 0 };
        _ctx.AddValidator(new TakenCheck(store));
    }

    protected override Component? Render() =>
        Form.Model(_model).Context(_ctx)[
            Input.Bind(() => _model.Username),
            Validation.Indicator.Template(static () => Span["Checking..."]).For(() => _model.Username),
            Validation.Message.Template(static messages => Div[messages[0]]).For(() => _model.Username)
        ];

    public sealed class Signup
    {
        public string Username { get; set; } = "";
    }

    private sealed class TakenCheck(HandlerWaitStore store) : IAsyncFieldValidator
    {
        public ValueTask Validate(EditContext context, CancellationToken ct) => ValueTask.CompletedTask;

        public async ValueTask ValidateField(EditContext context, FieldIdentifier field, CancellationToken ct)
        {
            await store.Save().ConfigureAwait(false);
            context.AddValidationMessage(field, "Already taken.");
        }
    }
}

/// <summary>
///     An ambient scope a handler opens around its reads, as <c>Tenant.Across()</c> does: it flows on an
///     <see cref="AsyncLocal{T}" />, and nothing outside the block it is opened in may see it.
/// </summary>
public static class HandlerWaitAcross
{
    private static readonly AsyncLocal<bool> Open = new();

    public static bool IsOpen => Open.Value;

    public static IDisposable Enter()
    {
        Open.Value = true;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Open.Value = false;
    }
}

/// <summary>
///     A page whose render branches on that scope — inside one there is no partner to draw for — and a handler that
///     reads inside one, through a helper that leaves the handler's context as a data layer does.
/// </summary>
[Route("/w/across")]
public sealed partial class HandlerWaitAcrossPage(HandlerWaitStore store) : Component
{
    private bool _showing;
    private int _shown;

    protected override Component? Render() =>
    [
        P.Id("partner")[HandlerWaitAcross.IsOpen ? "no partner" : "for the partner"],
        Button.Id("show").OnClick(Show)[_showing ? "showing" : "show"],
        P.Id("shown")[$"shown {_shown}"],
    ];

    private async Task Show()
    {
        _showing = true;
        using (HandlerWaitAcross.Enter())
        {
            await ReadAcross(store);

            // Halfway: the page is drawn again by the repaint that follows this continuation, as a busy button is.
            StateHasChanged();
            await ReadAcross(store);
        }

        _showing = false;
        _shown++;
    }

    private static async Task ReadAcross(HandlerWaitStore store) => await store.Save().ConfigureAwait(false);
}
