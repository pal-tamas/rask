using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;

namespace Rask.Mail.Tests;

/// <summary>A tiny email body component, to prove a Rask component renders to the HTML body.</summary>
public sealed class GreetingEmail(string name) : Component
{
    protected override Component? Render() => new Text($"Hello, {name}!");
}

/// <summary>
/// A fake <see cref="IMailSender"/> that records what it was asked to deliver and can be told to fail a
/// number of attempts (to exercise retry/dead-letter) or always fail.
/// </summary>
public sealed class RecordingMailSender : IMailSender
{
    private readonly List<OutgoingMail> _sent = [];
    private int _attempts;

    /// <summary>Fail this many attempts before succeeding.</summary>
    public int FailFirst { get; set; }

    /// <summary>Fail every attempt.</summary>
    public bool AlwaysFail { get; set; }

    /// <summary>
    /// When set, the send parks on <see cref="Release"/> instead of completing — standing in for an SMTP
    /// conversation still in flight when the host is asked to stop. <see cref="Entered"/> signals that the
    /// send has actually begun, so a test never races the poll loop.
    /// </summary>
    public TaskCompletionSource? Release { get; set; }

    /// <summary>Signalled once a gated send has started. Pairs with <see cref="Release"/>.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<OutgoingMail> Sent
    {
        get { lock (_sent) { return _sent.ToArray(); } }
    }

    public int Attempts => Volatile.Read(ref _attempts);

    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _attempts);
        if (AlwaysFail || n <= FailFirst)
        {
            throw new InvalidOperationException("smtp boom");
        }

        if (Release is { } gate)
        {
            Entered.TrySetResult();
            // Observes the token, so a grace expiry actually cancels the send — a sender that ignored its
            // token could not be cancelled at all and would prove nothing about the grace period.
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_sent) { _sent.Add(mail); }
    }
}

public sealed class MailDbContext(DbContextOptions<MailDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskMail();
}

/// <summary>A hand-rolled fake clock (no external package): the processor's due/backoff checks read it, so
/// tests drive time deterministically while the poll loop ticks on the real (short) interval.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>Builds a real-SQLite service provider wired for mail, with a controllable clock and a fake sender.</summary>
public sealed class MailHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public MailHarness(Action<MailOptions>? configure = null, RecordingMailSender? sender = null)
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"rask-mail-test-{Guid.NewGuid():N}.db");
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Sender = sender ?? new RecordingMailSender();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);   // registered first so AddRaskMail' TryAddSingleton keeps it
        services.AddSingleton<IMailSender>(Sender);    // ditto — overrides the built-in sender selection
        services.AddRaskMail<MailDbContext>(o =>
        {
            o.From = "noreply@example.com";
            o.FromName = "Example";
            o.PollInterval = TimeSpan.FromMilliseconds(20);
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<MailDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public string DbPath { get; }

    public FakeTimeProvider Clock { get; }

    public RecordingMailSender Sender { get; }

    public IMailQueue Queue => _provider.GetRequiredService<IMailQueue>();

    public IHostedService Processor =>
        _provider.GetServices<IHostedService>().OfType<MailProcessor<MailDbContext>>().Single();

    public MailDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<MailDbContext>>().CreateDbContext();

    public async Task<int> CountMailAsync()
    {
        await using var db = NewContext();
        return await db.Set<QueuedMail>().CountAsync();
    }

    public async Task<QueuedMail> SingleMailAsync()
    {
        await using var db = NewContext();
        return await db.Set<QueuedMail>().SingleAsync();
    }

    /// <summary>Polls <paramref name="condition"/> until true; advances the fake clock each tick so backed-off retries become due.</summary>
    public async Task WaitUntilAsync(Func<Task<bool>> condition, bool advanceClock = false, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            if (advanceClock)
            {
                Clock.Advance(TimeSpan.FromMinutes(2));
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }
    }
}
