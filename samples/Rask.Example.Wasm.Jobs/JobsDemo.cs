using Microsoft.EntityFrameworkCore;
using Rask.Jobs;
using Rask.SQLite.Browser;

namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     Queue a job, watch it run. The click only writes a row to the queue; the greeting below is
///     written later, by the job processor's poll loop, from a different DI scope.
/// </summary>
public sealed partial class JobsDemo : Component
{
    private readonly IJobQueue _queue;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly GreetingFeed _feed;
    private readonly DatabaseReady _ready;
    private readonly BrowserSqliteOwnership _ownership;

    private string _name = "world";
    private string _status = "";
    private bool _canTakeOver;
    private List<Greeting> _greetings = [];

    public JobsDemo(
        IJobQueue queue,
        IDbContextFactory<AppDbContext> factory,
        GreetingFeed feed,
        DatabaseReady ready,
        BrowserSqliteOwnership ownership)
    {
        _queue = queue;
        _factory = factory;
        _feed = feed;
        _ready = ready;
        _ownership = ownership;
    }

    protected override async Task OnMountAsync()
    {
        // The handler raises this from the processor's loop — an out-of-band re-render, with no click
        // anywhere near it.
        _feed.Updated += OnGreetingWritten;

        // Hosted services start after the first render on this host, so the schema does not exist yet
        // when this runs. Waiting is the documented way to turn "started" into "ready".
        // Ownership settles during the host's StartAsync, which runs after this first render — so wait
        // for it, or the banner would never appear in the tab that needs it.
        await _ownership.Resolved;

        if (_ownership.IsOwner == false)
        {
            // Fire-and-forget: this completes only when the other tab closes, which may be never.
            _ = WatchForTakeoverAsync();
        }

        await _ready.Ready;
        await LoadAsync();
    }

    protected override void OnUnmount() => _feed.Updated -= OnGreetingWritten;

    // The event is raised from the job processor's loop, so there is no caller to return a Task to. The
    // try/catch is not optional: a bare `_ = ReloadAsync()` would swallow a query failure and leave the
    // page silently stale, which looks identical to the job never having run.
    private void OnGreetingWritten() => _ = ReloadSafelyAsync();

    // Turns "close the other tab" into "reload now". Reloading is the only way to take over: this tab
    // already opened its own empty database at boot, and the file cannot be swapped under live connections.
    private async Task WatchForTakeoverAsync()
    {
        await _ownership.Available;
        _canTakeOver = true;
        StateHasChanged();
    }

    private async Task ReloadSafelyAsync()
    {
        try
        {
            await LoadAsync();
            StateHasChanged();
        }
#pragma warning disable CA1031 // Nothing above this can handle it; reporting is the whole point.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.WriteLine($"[JobsDemo] reloading after a job failed: {ex}");
        }
    }

    private async Task LoadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        _greetings = await db.Greetings.OrderByDescending(g => g.Id).Take(10).ToListAsync();
    }

    private async Task EnqueueAsync()
    {
        var name = string.IsNullOrWhiteSpace(_name) ? "world" : _name.Trim();
        await _queue.EnqueueAsync(new GreetJob(name));
        _status = $"Queued a job for \"{name}\". The processor will pick it up on its next poll.";
    }

    protected override Component? Render() =>
        Div[
            // Only once the election has settled: `null` means "still deciding", and showing this during
            // a normal boot would be a scary banner for a non-problem.
            _ownership.IsOwner == false
                ? _canTakeOver
                    ? Div.Class("notice ready").Data(new Dictionary<string, string?> { ["testid"] = "can-take-over" })[
                        Strong["Your data is ready."],
                        " The other tab has closed. Reload to use the database here — reloading is what "
                        + "takes it over, because this tab already opened an empty one at boot."]
                    : Div.Class("notice").Data(new Dictionary<string, string?> { ["testid"] = "not-owner" })[
                        Strong["Another tab has this database open."],
                        " Your data is safe — it just isn't reachable from here, because only one tab may "
                        + "own the file. Close the other tab and this will say so."]
                : null,
            Div.Class("row")[
                Input(
                    Type: InputType.Text,
                    Value: _name,
                    Placeholder: "a name to greet",
                    Data: new Dictionary<string, string?> { ["testid"] = "name" },
                    OnInput: v => _name = v),
                Button
                    .Type("button")
                    .Data(new Dictionary<string, string?> { ["testid"] = "enqueue" })
                    .OnClick(async () => await EnqueueAsync())["Queue a job"]
            ],
            P.Class("status").Data(new Dictionary<string, string?> { ["testid"] = "status" })[_status],
            H2["Greetings written by the job processor"],
            _greetings.Count == 0
                ? P.Class("empty").Data(new Dictionary<string, string?> { ["testid"] = "empty" })[
                    "Nothing yet — queue a job."]
                : Ul.Data(new Dictionary<string, string?> { ["testid"] = "greetings" })[
                    _greetings.Select(g =>
                        Li.Key(g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))[
                            Span[g.Text],
                            Time[g.CreatedAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)]
                        ])]
        ];
}
