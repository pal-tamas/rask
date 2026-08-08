using Microsoft.EntityFrameworkCore;
using Rask.Jobs;

namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     Queue a job, watch it run. The click only writes a row to the queue; the greeting below is
///     written later, by the job processor's poll loop, from a different DI scope.
/// </summary>
public sealed class JobsDemo : Component
{
    private readonly IJobQueue _queue;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly GreetingFeed _feed;
    private readonly DatabaseReady _ready;

    private string _name = "world";
    private string _status = "";
    private List<Greeting> _greetings = [];

    public JobsDemo(
        IJobQueue queue,
        IDbContextFactory<AppDbContext> factory,
        GreetingFeed feed,
        DatabaseReady ready)
    {
        _queue = queue;
        _factory = factory;
        _feed = feed;
        _ready = ready;
    }

    protected override async Task OnMountAsync()
    {
        // The handler raises this from the processor's loop — an out-of-band re-render, with no click
        // anywhere near it.
        _feed.Updated += OnGreetingWritten;

        // Hosted services start after the first render on this host, so the schema does not exist yet
        // when this runs. Waiting is the documented way to turn "started" into "ready".
        await _ready.Ready;
        await LoadAsync();
    }

    protected override void OnUnmount() => _feed.Updated -= OnGreetingWritten;

    // The event is raised from the job processor's loop, so there is no caller to return a Task to. The
    // try/catch is not optional: a bare `_ = ReloadAsync()` would swallow a query failure and leave the
    // page silently stale, which looks identical to the job never having run.
    private void OnGreetingWritten() => _ = ReloadSafelyAsync();

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
        Div()[
            Div(Class: "row")[
                Input(
                    Type: InputType.Text,
                    Value: _name,
                    Placeholder: "a name to greet",
                    Data: new Dictionary<string, string?> { ["testid"] = "name" },
                    OnInput: v => _name = v),
                Button(
                    Type: "button",
                    Data: new Dictionary<string, string?> { ["testid"] = "enqueue" },
                    OnClick: async () => await EnqueueAsync())["Queue a job"]
            ],
            P(Class: "status", Data: new Dictionary<string, string?> { ["testid"] = "status" })[_status],
            H2()["Greetings written by the job processor"],
            _greetings.Count == 0
                ? P(Class: "empty", Data: new Dictionary<string, string?> { ["testid"] = "empty" })[
                    "Nothing yet — queue a job."]
                : Ul(Data: new Dictionary<string, string?> { ["testid"] = "greetings" })[
                    _greetings.Select(g =>
                        Li(Key: g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))[
                            Span()[g.Text],
                            Time()[g.CreatedAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)]
                        ])]
        ];
}
