using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What each battery flag puts in the scaffolded project. RaskApp wires every battery itself — the
/// registrations, their tables and the pipeline are tested in Rask.Server.Tests — so a battery the app does
/// without is one <c>c.X.Off()</c> line in Program.cs, and the pages and settings are what is left here.
/// </summary>
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

    /// <summary>The same, on the wasm-hosted template — the host that serves a browser app.</summary>
    private static Dictionary<string, string> GenerateHosted(params string[] flags) =>
        ProjectGenerator.GenerateWasmHosted(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    // Every flag, so each row can take exactly one battery away from a full app.
    private static readonly string[] Every =
    [
        "data", "cqrs", "jobs", "mail", "cache", "storage", "outbox", "push", "pwa", "snapshots", "logs", "ops",
    ];

    // The batteries nothing else depends on, with the name Program.cs switches them off by.
    public static TheoryData<string, string> Leaves => new()
    {
        { "outbox", "Outbox" },
        { "jobs", "Jobs" },
        { "mail", "Mail" },
        { "cache", "Cache" },
        { "storage", "Storage" },
        { "snapshots", "Snapshots" },
        { "ops", "Ops" },
        { "logs", "Logs" },
        { "push", "Push" },
    };

    public static TheoryData<string> AuthPages =>
    [
        "LoginPage.cs", "RegisterPage.cs", "LogoutPage.cs", "ForgotPasswordPage.cs",
        "ResetPasswordPage.cs", "ConfirmEmailPage.cs", "DevicesPage.cs",
    ];

    [Theory]
    [MemberData(nameof(AuthPages))]
    public void An_app_with_accounts_gets_the_sign_in_pages_as_its_own_code(string page)
    {
        // The pages are the app's to restyle and edit, the way a starter kit writes them, and they are written
        // against IAuth, so the flows themselves keep coming from Rask.Auth.
        var files = Generate("data");

        var source = files["Features/Auth/" + page];
        Assert.Contains("namespace App.Features.Auth;", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/", source, StringComparison.Ordinal);

        // A build-only gate found the first cut of these missing this: the text looked right and did not compile.
        if (source.Contains("IAuth", StringComparison.Ordinal) || source.Contains("IUserProvider", StringComparison.Ordinal))
        {
            Assert.Contains("using Rask.Core.Authentication;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_sign_in_and_device_pages_offer_passkeys()
    {
        var files = Generate("data");

        // A passkey is another way in, so the sign-in page has to offer it beside the password, and the device
        // list is where one is added and taken away. Both go through IAuth, so the ceremony stays in Rask.Auth.
        var login = files["Features/Auth/LoginPage.cs"];
        Assert.Contains("SignInWithPasskeyAsync", login, StringComparison.Ordinal);
        Assert.Contains("Sign in with a passkey", login, StringComparison.Ordinal);

        var devices = files["Features/Auth/DevicesPage.cs"];
        Assert.Contains("AddPasskeyAsync", devices, StringComparison.Ordinal);
        Assert.Contains("RemovePasskeyAsync", devices, StringComparison.Ordinal);

        // The support check is a browser call, so both pages must name the API they inject it from.
        foreach (var page in new[] { login, devices })
        {
            Assert.Contains("using Rask.Core.Browser;", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_register_page_sets_the_users_own_columns_while_registering()
    {
        var register = Generate("data")["Features/Auth/RegisterPage.cs"];

        Assert.Contains("(User user) => user.Rename(model.DisplayName)", register, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_without_a_database_gets_no_sign_in_pages()
    {
        Assert.DoesNotContain(Generate().Keys, k => k.StartsWith("Features/Auth/", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Leaves))]
    public void A_battery_turned_off_is_one_line_in_Program_cs(string flag, string battery)
    {
        var without = Every.Where(f => f != flag).ToArray();

        var off = Generate(without)["Program.cs"];
        var on = Generate(Every)["Program.cs"];

        Assert.Contains($"app.Configure(c => c.{battery}.Off());", off, StringComparison.Ordinal);
        Assert.DoesNotContain($"c.{battery}.Off();", on, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("jobs")]
    [InlineData("cache")]
    public void Every_app_with_a_database_gets_a_User_of_its_own(string flag)
    {
        // Rask ships no user type. A generator wires the accounts to whichever Authenticatable the app
        // declares, so the app has to declare one — and retrofitting it later is a migration, which is why it
        // is here from the first commit.
        var files = Generate(flag);

        var user = files["Features/Shared/User.cs"];
        Assert.Contains("public sealed class User : Authenticatable", user, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore.Identity", user, StringComparison.Ordinal);
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
    public void Push_keeps_the_pwa_it_needs_on()
    {
        // --push implies --pwa: subscribing needs the service worker the PWA registration installs.
        var files = Generate("push");

        var program = files["Program.cs"];

        Assert.DoesNotContain("c.Pwa.Off()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("c.Push.Off()", program, StringComparison.Ordinal);
        Assert.Contains("wwwroot/icon.svg", files.Keys);
    }

    [Fact]
    public void Every_pillar_composes_into_one_app_with_none_of_its_wiring_in_the_scaffold()
    {
        // The context and the push endpoints are RaskApp's now (RaskAppDbContext, /_rask/push), so an app
        // with every battery on has one line of Program.cs and neither file.
        var files = Generate([.. Every, "docker"]);

        var program = files["Program.cs"];

        Assert.Contains("RaskApp.Create(args).Run<App>();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("var app =", program, StringComparison.Ordinal);
        Assert.Contains("Dockerfile", files.Keys);
        Assert.DoesNotContain("Features/Shared/AppDbContext.cs", files.Keys);
        Assert.DoesNotContain("Features/Push/PushSubscriptions.cs", files.Keys);
    }

    // ── The log store ───────────────────────────────────────────────────────────────────────────────
    // Alone among the batteries it keeps a file of its own, which is why none of the assertions above fit it.

    [Fact]
    public void The_log_store_reads_its_own_connection_string()
    {
        // Rask:ConnectionStrings:Logs, not :App — a store that shared the application's connection string would
        // put a high-frequency writer back on the very file this design exists to keep it off. `rask deploy`
        // sets this to a path on the mounted volume.
        var files = Generate("logs");

        var settings = files["appsettings.json"];

        Assert.Contains("\"Logs\": \"Data Source=logs.db\"", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every value the scaffold chooses is a setting, so it lands in appsettings.json under Rask — where an
    /// environment variable can override it — and the file stays loadable with every battery's region kept.
    /// </summary>
    [Fact]
    public void Every_battery_setting_lands_in_appsettings_and_the_file_still_loads()
    {
        var files = GenerateHosted("cqrs", "data", "mail", "snapshots", "logs", "push", "pwa");
        var settings = files["appsettings.json"];

        var options = new System.Text.Json.JsonDocumentOptions
        {
            CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        using var document = System.Text.Json.JsonDocument.Parse(settings, options);
        var rask = document.RootElement.GetProperty("Rask");

        Assert.Equal("Data Source=app.db", rask.GetProperty("ConnectionStrings").GetProperty("App").GetString());
        Assert.Equal("Data Source=logs.db", rask.GetProperty("ConnectionStrings").GetProperty("Logs").GetString());
        Assert.True(rask.GetProperty("Sqlite").GetProperty("StrictTables").GetBoolean());
        Assert.Equal("no-reply@example.com", rask.GetProperty("Mail").GetProperty("From").GetString());
        Assert.Equal("06:00:00", rask.GetProperty("Snapshots").GetProperty("Interval").GetString());
        Assert.Equal(7, rask.GetProperty("Snapshots").GetProperty("Retain").GetInt32());
        Assert.Equal("mailto:admin@example.com", rask.GetProperty("WebPush").GetProperty("Subject").GetString());

        // A wasm-hosted host renders no pages of its own, so it tunes no live runtime and ships no culture list:
        // those sections would be settings nothing reads.
        Assert.False(rask.TryGetProperty("Server", out _));
        Assert.False(rask.TryGetProperty("Culture", out _));

        // …and none of the old spellings survive in the code that used to read them.
        var program = files["Program.cs"];

        // Nor does either file's settings note name AddRask, which a wasm-hosted host never calls.
        Assert.DoesNotContain("AddRask reads", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRask reads", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConnectionString(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Litestream:ReplicaUrl\"", program, StringComparison.Ordinal);
        Assert.DoesNotContain("\"WebPush:PublicKey\"", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Mail:PickupDirectory", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite:SnapshotDirectory", program, StringComparison.Ordinal);
        Assert.DoesNotContain("o.From =", program, StringComparison.Ordinal);
    }

    [Fact]
    public void A_battery_that_is_off_leaves_no_section_behind()
    {
        var settings = Generate()["appsettings.json"];

        Assert.DoesNotContain("\"Mail\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Snapshots\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"WebPush\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Litestream\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("rask:", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one battery that does not drag the database in behind it. An app with no EF Core and no
    /// migrations can still keep its log — and if this regressed, <c>--logs</c> would silently turn on a
    /// whole data layer nobody asked for.
    /// </summary>
    [Fact]
    public void The_log_store_does_not_imply_a_database()
    {
        var files = Generate("logs");

        var program = files["Program.cs"];

        Assert.Contains("c.Data.Off();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("c.Logs.Off()", program, StringComparison.Ordinal);
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

    [Fact]
    public void The_next_steps_teach_declaring_a_model_not_adding_a_DbSet()
    {
        // What the reader is told to do first is what they will do. Pointing them at a DbSet teaches the
        // one workflow this data layer exists to remove.
        var next = ProjectGenerator.GenerateServer(
            Root, "App", NewCommand.BatteriesOf(["jobs"]), Version).Notes ?? "";

        Assert.Contains("Aggregate<Guid>", next, StringComparison.Ordinal);
        Assert.DoesNotContain("Add a DbSet", next, StringComparison.Ordinal);
    }
}
