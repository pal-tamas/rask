using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     The app a stale page was reported from: a layout whose header holds a keyed component that follows a
///     singleton's event, above the Outlet, and a list page that follows it too, whose filter goes to its own
///     address with a new query and whose rows are read in <c>OnUpdated</c>.
/// </summary>
public sealed partial class BoardApp : Component
{
    protected override Component? HeadAssets => Markup.Title["board"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>
///     The singleton the pages follow. Its event is raised by the test from threads of its own, as a
///     background poller raises it: synchronously, from several threads at once, with no dispatcher.
/// </summary>
public sealed class PumpBoard
{
    private TaskCompletionSource? _held;
    private Action<int>? _onWalk;
    private int _walks;
    private int _value;

    public event Action? Changed;

    /// <summary>What every follower draws.</summary>
    public int Value => Volatile.Read(ref _value);

    /// <summary>How many times the list page has rendered since <see cref="Arm" />.</summary>
    public int Walks => Volatile.Read(ref _walks);

    /// <summary>How many times the page that asks for a render from every render has rendered.</summary>
    public int RunawayRenders;

    /// <summary>Run by the filter's handler before it navigates.</summary>
    public Action? InHandler { get; set; }

    /// <summary>Counts the list page's renders from one, and runs <paramref name="onWalk" /> inside each.</summary>
    public void Arm(Action<int> onWalk)
    {
        Volatile.Write(ref _walks, 0);
        Volatile.Write(ref _onWalk, onWalk);
    }

    /// <summary>Called by the list page as its <c>Render</c> ends: the walk is past the header, and the page has read its state.</summary>
    public void Walked()
    {
        var walk = Interlocked.Increment(ref _walks);
        Volatile.Read(ref _onWalk)?.Invoke(walk);
    }

    /// <summary>
    ///     Sets the value and raises the event from <paramref name="threads" /> threads at once, none of them
    ///     the caller's, and returns when all of them have.
    /// </summary>
    public void Burst(int value, int threads = 4)
    {
        Volatile.Write(ref _value, value);
        OnThreadsOfTheirOwn(threads, () => Changed?.Invoke());
    }

    /// <summary>Holds every read from now until <see cref="ReleaseFromOutside" />.</summary>
    public void Hold() => _held = new TaskCompletionSource();

    /// <summary>
    ///     Lets the held read go from a thread of its own. Its continuations run on that thread, so the page
    ///     has its rows and has asked for its render by the time this returns.
    /// </summary>
    public void ReleaseFromOutside()
    {
        if (Interlocked.Exchange(ref _held, null) is { } held)
        {
            OnThreadsOfTheirOwn(1, held.SetResult);
        }
    }

    public async Task<string[]> Rows(string? from)
    {
        if (_held is { } held)
        {
            await held.Task.ConfigureAwait(false);
        }

        return [.. Enumerable.Range(1, 3).Select(n => $"row {n} from {from ?? "ever"}.")];
    }

    private static void OnThreadsOfTheirOwn(int count, Action work)
    {
        var threads = Enumerable.Range(0, count).Select(_ => new Thread(() => work())).ToArray();
        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }
    }
}

[Route("/board")]
public sealed partial class BoardLayout : Component
{
    protected override Component? Render() =>
    [
        Header[BoardNotices.Key("notices")],
        Main[Outlet],
    ];
}

/// <summary>The header's follower: there on every page of the layout, so it survives every navigation.</summary>
public sealed partial class BoardNotices(PumpBoard board) : Component
{
    protected override Task OnMount()
    {
        board.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        board.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => P.Id("notices")[$"notices {board.Value}."];
}

/// <summary>The list: its filter is in its address, and its rows are read when the address changes.</summary>
[Route("list")]
[ParentRoute(typeof(BoardLayout))]
public sealed partial class BoardListPage(PumpBoard board) : Component
{
    private string[]? _rows;

    [QueryParam]
    public string? From { get; set; }

    protected override Task OnMount()
    {
        board.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        board.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override async Task OnUpdated()
    {
        var asked = From;
        var rows = await board.Rows(asked).ConfigureAwait(false);
        if (asked == From)
        {
            _rows = rows;
        }
    }

    protected override Component? Render()
    {
        Component drawn =
        [
            Button.Id("filter").OnClick(Filter)["filter"],
            P.Id("page")[$"page {board.Value}."],
            Ul.Id("rows")[(_rows ?? []).Select(static row => (Component)Li.Key(row)[row]).ToArray()],
        ];

        // After the page has read what it draws: whatever the test changes now is the next render's to show.
        board.Walked();
        return drawn;
    }

    private void Filter()
    {
        board.InHandler?.Invoke();
        Go.With("from", "october");
    }
}

/// <summary>A page that asks for a render from every render.</summary>
[Route("runaway")]
[ParentRoute(typeof(BoardLayout))]
public sealed partial class BoardRunawayPage(PumpBoard board) : Component
{
    protected override Component? Render()
    {
        var renders = Interlocked.Increment(ref board.RunawayRenders);
        StateHasChanged();
        return P.Id("runaway")[$"runaway {renders}."];
    }
}
