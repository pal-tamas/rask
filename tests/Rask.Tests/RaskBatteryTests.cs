using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Mail;

namespace Rask.Tests;

/// <summary>
///     Every battery the <c>Rask</c> package brings is on; <c>Program.cs</c> is where one is turned off.
/// </summary>
/// <remarks>
///     Asserted on the hosted services the app ends up with, rather than on any intermediate on/off record.
///     Each database-backed battery contributes one distinctly named background worker, so the set of them
///     is a direct reading of what was actually wired — and it fails the same way a user would notice, with
///     a processor that is or is not running.
/// </remarks>
public sealed class RaskBatteryTests
{
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    // The workers each battery adds. Names rather than types because the types are internal to their
    // packages, and the name is what an operator sees in a log line anyway.
    private const string Jobs = "JobProcessor`1";
    private const string Mail = "MailProcessor`1";
    private const string Cache = "CachePurger`1";
    private const string Outbox = "OutboxProcessor`1";

    private static HashSet<string> Workers(Action<RaskApp>? arrange = null, bool withDatabase = true)
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        arrange?.Invoke(app);

        if (withDatabase)
        {
            // The app names its own context, exactly as a real Program.cs does. Nothing else tells Rask
            // which database the pillars belong to — it reads this registration back.
            app.Services.AddDbContextFactory<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        }

        var built = app.Build<TestApp>();
        return built.Services.GetServices<IHostedService>()
            .Select(s => s.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void An_app_that_says_nothing_gets_every_battery()
    {
        // The headline: a Program.cs with no Configure block is an app with all of them running. The
        // absence of code is the meaning.
        var workers = Workers();

        Assert.Contains(Jobs, workers);
        Assert.Contains(Mail, workers);
        Assert.Contains(Cache, workers);
        Assert.Contains(Outbox, workers);
    }

    [Fact]
    public void Turning_one_off_leaves_the_rest_alone()
    {
        var workers = Workers(a => a.Configure(c => c.Jobs.Off()));

        Assert.DoesNotContain(Jobs, workers);
        Assert.Contains(Mail, workers);
        Assert.Contains(Cache, workers);
        Assert.Contains(Outbox, workers);
    }

    [Fact]
    public void Turning_the_database_off_takes_its_dependents_with_it()
    {
        // They all register as AddRaskX<TContext> and resolve IDbContextFactory<TContext>. Leaving them on
        // without a database would wire pillars onto a context that is not there.
        var workers = Workers(a => a.Configure(c => c.Data.Off()));

        Assert.DoesNotContain(Jobs, workers);
        Assert.DoesNotContain(Mail, workers);
        Assert.DoesNotContain(Cache, workers);
        Assert.DoesNotContain(Outbox, workers);
    }

    [Fact]
    public void An_app_with_no_DbContext_gets_no_database_batteries_and_still_starts()
    {
        // Nothing is guessed at and nothing throws: an app that registered no context simply has no
        // pillars, and the rest of it runs.
        var workers = Workers(withDatabase: false);

        Assert.DoesNotContain(Jobs, workers);
        Assert.DoesNotContain(Outbox, workers);
    }

    [Fact]
    public void A_battery_is_configured_where_it_is_turned_off()
    {
        // The other half of the block: Off() and setup live together, so there is one place to read how
        // this app differs. The recorded delegate is replayed onto the battery's own options instance.
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Configure(c => c.Mail.Configure(o => o.From = "no-reply@example.test"));
        app.Services.AddDbContextFactory<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));

        var built = app.Build<TestApp>();

        Assert.Equal(
            "no-reply@example.test",
            built.Services.GetRequiredService<MailOptions>().From);
    }

    [Fact]
    public void An_app_that_wires_a_battery_itself_wins()
    {
        // Why nothing has to be turned off in order to configure it. The automatic wiring runs last and
        // every AddRaskX is idempotent, keeping the first registration's options — so a direct call in
        // Program.cs is the one that takes effect, and the later one adds nothing.
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"));
        app.Services.AddDbContextFactory<TestDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        app.Services.AddRaskMail<TestDbContext>(o => o.From = "chosen-by-hand@example.test");

        var built = app.Build<TestApp>();

        Assert.Equal(
            "chosen-by-hand@example.test",
            built.Services.GetRequiredService<MailOptions>().From);

        // And exactly one worker, not two: AddHostedService uses TryAddEnumerable.
        Assert.Single(built.Services.GetServices<IHostedService>(), s => s.GetType().Name == Mail);
    }
}
