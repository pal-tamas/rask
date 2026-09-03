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
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
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

    [Theory]
    [InlineData("outbox")]
    [InlineData("data")]
    public void AddRaskData_is_scaffolded_bare_whether_or_not_the_outbox_is_on(string flag)
    {
        // The outbox used to require `o.DispatchDomainEventsInProcess = false` here, and a scaffold that
        // forgot it silently emptied the outbox: DomainEventInterceptor drained and cleared every entity's
        // events before OutboxInterceptor could copy them, while every handler still ran, so nothing looked
        // wrong. The framework now settles that when the container is built (AddRaskOutbox registers an
        // IDomainEventDeliveryOwner), so the emitter has no argument left to get wrong. Asserting the
        // ABSENCE is the point — this is the line that would regress if the old conditional came back.
        var program = Generate(flag)["Program.cs"];

        Assert.Contains("builder.Services.AddRaskData();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchDomainEventsInProcess", program, StringComparison.Ordinal);
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
        // Not a correctness rule — routing matches on precedence, so mapping after UseRask would work
        // too (RaskAppTests.An_endpoint_mapped_after_UseRask_still_runs pins that). This pins the
        // scaffold's LAYOUT: endpoints read in one place, above the line that ends the pipeline.
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
    public void Every_pillar_composes_into_one_app()
    {
        var files = Generate(
            "data", "cqrs", "jobs", "mail", "cache", "outbox", "push", "pwa", "snapshots", "logs", "ops",
            "auth", "docker");
        var program = files["Program.cs"];

        foreach (var registration in new[]
        {
            "AddRaskCqrs()", "AddRaskData(", "AddRaskOutbox<AppDbContext>()", "AddDbContextFactory<AppDbContext>",
            "AddRaskJobs<AppDbContext>()", "AddRaskMail<AppDbContext>(", "AddRaskCache<AppDbContext>()",
            "AddRaskSqliteSnapshots(", "AddRaskSqliteLitestream(", "AddRaskWebPush(", "AddRaskPwa(",
            "AddRaskLogging(", "AddRaskDashboard<AppDbContext>()",
            "AddAuthentication(",
        })
        {
            Assert.Contains(registration, program, StringComparison.Ordinal);
        }

        Assert.Contains("Dockerfile", files.Keys);
        Assert.Contains("Features/Shared/AppDbContext.cs", files.Keys);
        Assert.Contains("Features/Push/PushSubscriptions.cs", files.Keys);
    }

    // ── The log store ───────────────────────────────────────────────────────────────────────────────
    // Alone among the batteries it keeps a file of its own, which is why none of the assertions above fit it.

    [Fact]
    public void The_log_store_adds_its_package_and_registration()
    {
        var files = Generate("logs");

        Assert.Contains(
            $"""<PackageReference Include="Rask.Logging" Version="{Version}"/>""",
            files["App.csproj"],
            StringComparison.Ordinal);
        Assert.Contains("using Rask.Logging;", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddRaskLogging(", files["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_store_reads_its_own_connection_string()
    {
        // ConnectionStrings:Logs, not :App — a store that shared the application's connection string would
        // put a high-frequency writer back on the very file this design exists to keep it off. `rask deploy`
        // sets this to a path on the mounted volume.
        var program = Generate("logs")["Program.cs"];

        Assert.Contains("""GetConnectionString("Logs")""", program, StringComparison.Ordinal);
        Assert.Contains("?? \"Data Source=logs.db\"", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one battery that does not drag the database in behind it. An app with no EF Core, no
    /// <c>AppDbContext</c> and no migrations can still keep its log — and if this regressed, <c>--logs</c>
    /// would silently scaffold a whole data layer nobody asked for.
    /// </summary>
    [Fact]
    public void The_log_store_does_not_imply_a_database()
    {
        var files = Generate("logs");

        Assert.DoesNotContain("Features/Shared/AppDbContext.cs", files.Keys);
        Assert.DoesNotContain("AddRaskData(", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("AddDbContextFactory", files["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<PackageReference Include=\"Rask.Data\"",
            files["App.csproj"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_log_store_says_out_loud_that_its_file_is_not_backed_up()
    {
        // The scaffolded comment is the only place a reader learns the trade-off before they need it.
        var program = Generate("logs")["Program.cs"];

        Assert.Contains("NOT covered by `rask db backup`", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// The migration warning has moved out of here, and that is the point: <c>rask new</c> creates and
    /// applies the first migration itself, so by the time this text is printed the tables already exist.
    /// The command prints the manual pair only when it could not run them — pinned in
    /// <c>NewCommandTests.Skipping_the_restore_says_the_migration_still_has_to_happen</c>.
    /// </summary>
    [Fact]
    public void The_next_steps_no_longer_tell_you_to_migrate_before_the_first_run()
    {
        var next = ProjectGenerator.GenerateServer(
            Root, "App", NewCommand.BatteriesOf(["jobs"]), Version).Notes ?? "";

        Assert.DoesNotContain("rask db add Init", next, StringComparison.Ordinal);
        Assert.DoesNotContain("exit on a missing table", next, StringComparison.Ordinal);
    }
}
