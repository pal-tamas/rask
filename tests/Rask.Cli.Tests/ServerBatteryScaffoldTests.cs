using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What each battery flag puts in the scaffolded project — the package reference, the DI registration, and
/// the <c>OnModelCreating</c> call that gives the pillar its tables.
/// </summary>
/// <remarks>
/// The ordering assertions here are the valuable ones. A registration in the wrong place doesn't fail to
/// compile and doesn't throw — it produces an app that looks fine and quietly does the wrong thing.
/// </remarks>
public sealed class ServerBatteryScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    // Flags in, files out — the same path `rask new` takes, so the flag names are under test too.
    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.ToBatteries(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    public static TheoryData<string, string, string, string> Pillars => new()
    {
        { "jobs", "Rask.Jobs", "AddRaskJobs<AppDbContext>()", "modelBuilder.AddRaskJobs();" },
        { "mail", "Rask.Mail", "AddRaskMail<AppDbContext>(", "modelBuilder.AddRaskMail();" },
        { "cache", "Rask.Cache", "AddRaskCache<AppDbContext>()", "modelBuilder.AddRaskCache();" },
        { "outbox", "Rask.Outbox", "AddRaskOutbox<AppDbContext>()", "modelBuilder.AddRaskOutbox();" },
    };

    [Theory]
    [MemberData(nameof(Pillars))]
    public void A_database_backed_battery_adds_its_package_registration_and_schema(
        string flag, string package, string registration, string schemaCall)
    {
        var files = Generate(flag);

        Assert.Contains($"""<PackageReference Include="{package}" Version="{Version}"/>""", files["App.csproj"], StringComparison.Ordinal);
        Assert.Contains(registration, files["Program.cs"], StringComparison.Ordinal);

        // Without the OnModelCreating call the pillar's table never reaches a migration, and its processor
        // faults on a missing table at startup — which, for a hosted service, stops the whole host.
        Assert.Contains(schemaCall, files["Features/Shared/AppDbContext.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_outbox_turns_off_the_in_process_domain_event_publisher()
    {
        // The silent trap: with the in-process publisher left on, DomainEventInterceptor drains and clears
        // every entity's events before OutboxInterceptor can copy them. The outbox table stays empty and
        // delivery quietly stops being durable — while every handler still runs, so nothing looks wrong.
        var program = Generate("outbox")["Program.cs"];

        Assert.Contains("o.DispatchDomainEventsInProcess = false;", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_outbox_domain_events_keep_their_in_process_default()
    {
        var program = Generate("data")["Program.cs"];

        Assert.Contains("builder.Services.AddRaskData();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchDomainEventsInProcess", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_outbox_is_registered_before_the_DbContext_factory()
    {
        // Registration order IS interception order: OutboxInterceptor has to be in the container before the
        // factory callback resolves ISaveChangesInterceptor, or it never joins the SaveChanges pipeline.
        var program = Generate("outbox")["Program.cs"];

        Assert.True(
            program.IndexOf("AddRaskOutbox<AppDbContext>()", StringComparison.Ordinal) <
            program.IndexOf("AddDbContextFactory<AppDbContext>", StringComparison.Ordinal),
            "AddRaskOutbox must precede AddDbContextFactory.");
    }

    [Fact]
    public void Rask_conventions_are_applied_after_the_entity_configurations()
    {
        // ApplyRaskConventions walks the model as it stands. Run before the configurations, every entity
        // added afterwards silently misses the soft-delete filter and the concurrency token.
        var context = Generate("data")["Features/Shared/AppDbContext.cs"];

        // Anchored on the receiver: the explanatory comment names both methods above the calls, so a bare
        // name would match the prose instead of the code.
        Assert.True(
            context.IndexOf("modelBuilder.ApplyConfigurationsFromAssembly", StringComparison.Ordinal) <
            context.IndexOf("modelBuilder.ApplyRaskConventions", StringComparison.Ordinal),
            "ApplyRaskConventions must follow ApplyConfigurationsFromAssembly.");
    }

    [Fact]
    public void A_pillar_registration_follows_the_DbContext_factory_it_resolves()
    {
        var program = Generate("jobs", "mail", "cache")["Program.cs"];
        var factory = program.IndexOf("AddDbContextFactory<AppDbContext>", StringComparison.Ordinal);

        foreach (var registration in new[] { "AddRaskJobs<AppDbContext>()", "AddRaskMail<AppDbContext>(", "AddRaskCache<AppDbContext>()" })
        {
            Assert.True(
                factory < program.IndexOf(registration, StringComparison.Ordinal),
                $"{registration} should follow AddDbContextFactory.");
        }
    }

    [Fact]
    public void A_database_app_does_not_download_the_litestream_binary_at_build_time()
    {
        // Rask.SQLite.Litestream's build props fetch the binary from GitHub releases unless told not to,
        // so without this a scaffolded app can't be built offline — and errors outright on a RID with no
        // published asset. The binary belongs in the Docker image, which `--docker` already copies it into.
        Assert.Contains(
            "<RaskLitestreamDownload>false</RaskLitestreamDownload>",
            Generate("data")["App.csproj"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Push_maps_its_endpoints_before_the_UseRask_catch_all()
    {
        // UseRask serves the SPA for anything unmatched, so a minimal API mapped after it is unreachable.
        var files = Generate("push");
        var program = files["Program.cs"];

        Assert.True(
            program.IndexOf("app.MapPushSubscriptions();", StringComparison.Ordinal) <
            program.IndexOf("app.UseRask<App>();", StringComparison.Ordinal),
            "Push endpoints must be mapped before UseRask.");

        Assert.Contains("Features/Push/PushSubscriptions.cs", files.Keys);

        // --push implies --pwa: subscribing needs the service worker the PWA registration installs.
        Assert.Contains("AddRaskPwa", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_private_vapid_key_is_never_served_to_the_browser()
    {
        var endpoints = Generate("push")["Features/Push/PushSubscriptions.cs"];

        Assert.Contains("publicKey", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateKey", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void All_batteries_wires_every_pillar_into_one_app()
    {
        var files = Generate("all-batteries", "auth", "docker");
        var program = files["Program.cs"];

        foreach (var registration in new[]
        {
            "AddRaskCqrs()", "AddRaskData(", "AddRaskOutbox<AppDbContext>()", "AddDbContextFactory<AppDbContext>",
            "AddRaskJobs<AppDbContext>()", "AddRaskMail<AppDbContext>(", "AddRaskCache<AppDbContext>()",
            "AddRaskSqliteSnapshots(", "AddRaskSqliteLitestream(", "AddRaskWebPush(", "AddRaskPwa(",
            "AddAuthentication(",
        })
        {
            Assert.Contains(registration, program, StringComparison.Ordinal);
        }

        Assert.Contains("Dockerfile", files.Keys);
        Assert.Contains("Features/Shared/AppDbContext.cs", files.Keys);
        Assert.Contains("Features/Push/PushSubscriptions.cs", files.Keys);
    }

    [Fact]
    public void All_batteries_next_steps_call_out_the_migration_the_pillars_need()
    {
        // The pillars' tables only exist once a migration has been applied, and a faulted BackgroundService
        // stops the host — so "I ran it before migrating" shows up as the app exiting, not a friendly error.
        var next = ProjectGenerator.GenerateServer(
            Root, "App", NewCommand.ToBatteries(["jobs"]), Version).Notes ?? "";

        Assert.Contains("rask db add Init", next, StringComparison.Ordinal);
        Assert.Contains("exit on a missing table", next, StringComparison.Ordinal);
    }
}
