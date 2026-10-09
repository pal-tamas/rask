using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Cqrs;
using Rask.Data;
using Rask.Querying;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

/// <summary>An existing table with one unique rule, filtered the way the real one is.</summary>
public sealed class Wharf : Aggregate<int>
{
    public const string NameTaken = "Ilyen néven már létezik rakpart.";

    private Wharf() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Wharf Named(string name) => new() { Name = name };

    public static void Configure(EntityTypeBuilder<Wharf> builder)
    {
        builder.ToTable("Wharves");
        builder.HasIndex(w => new { w.Name, w.TenantId }).IsUnique(NameTaken).HasFilter("[TenantId] IS NOT NULL");
    }
}

/// <summary>An existing table whose unique rule is over two columns the form binds.</summary>
public sealed class Crane : Aggregate<int>
{
    public const string PlaceTaken = "Ezen a rakparton már áll daru ezen a helyen.";

    private Crane() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public string Pier { get; private set; } = "";

    public string Slot { get; private set; } = "";

    public static Crane At(string pier, string slot) => new() { Pier = pier, Slot = slot };

    public static void Configure(EntityTypeBuilder<Crane> builder)
    {
        builder.ToTable("Cranes");
        builder.HasIndex(c => new { c.Pier, c.Slot, c.TenantId }).IsUnique(PlaceTaken);
    }
}

public sealed class LegacyWharf
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int? TenantId { get; set; }
}

public sealed class LegacyCrane
{
    public int Id { get; set; }

    public string Pier { get; set; } = "";

    public string Slot { get; set; } = "";

    public int? TenantId { get; set; }
}

/// <summary>The context the app already had. It owns the schema, indexes included.</summary>
public sealed class LegacyHarbourContext(DbContextOptions<LegacyHarbourContext> options) : DbContext(options)
{
    public DbSet<LegacyWharf> Wharves => Set<LegacyWharf>();

    public DbSet<LegacyCrane> Cranes => Set<LegacyCrane>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyWharf>(b =>
        {
            b.ToTable("Wharves");
            b.Property(w => w.Name).HasMaxLength(255);
            b.HasIndex(w => new { w.Name, w.TenantId }).IsUnique().HasFilter("[TenantId] IS NOT NULL");
        });

        modelBuilder.Entity<LegacyCrane>(b =>
        {
            b.ToTable("Cranes");
            b.Property(c => c.Pier).HasMaxLength(100);
            b.Property(c => c.Slot).HasMaxLength(100);
            b.HasIndex(c => new { c.Pier, c.Slot, c.TenantId }).IsUnique();
        });
    }
}

public sealed class HarbourDomainContext(DbContextOptions<HarbourDomainContext> options) : RaskDbContext(options);

public sealed class WharfDraft
{
    public string? Name { get; set; }
}

public sealed class CraneDraft
{
    public string? Pier { get; set; }

    public string? Slot { get; set; }
}

/// <summary>The page as an app writes it: a bound field with nothing validating it, and a save that just saves.</summary>
public sealed partial class WharfFormApp : Component
{
    private readonly WharfDraft _model = new();
    private string _saved = "";

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(Save)[f =>
        [
            Markup.P[$"fault={f.Error?.GetType().Name ?? "none"}"],
            Markup.P[$"saved={_saved}"],
            Input.Bind(() => _model.Name).Id("name"),
            Validation.Message.Template(messages => Markup.P.Class("name-error")[messages[0]]).For(() => _model.Name),
            Button["save"],
        ]];

    private async Task Save(WharfDraft draft)
    {
        await Wharf.Named(draft.Name!).Save();
        _saved = draft.Name!;
    }
}

public sealed partial class CraneFormApp : Component
{
    private readonly CraneDraft _model = new();

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(Save)[f =>
        [
            Markup.P[$"fault={f.Error?.GetType().Name ?? "none"}"],
            Input.Bind(() => _model.Pier).Id("pier"),
            Validation.Message.Template(messages => Markup.P.Class("pier-error")[messages[0]]).For(() => _model.Pier),
            Input.Bind(() => _model.Slot).Id("slot"),
            Validation.Message.Template(messages => Markup.P.Class("slot-error")[messages[0]]).For(() => _model.Slot),
            Button["save"],
        ]];

    private static Task Save(CraneDraft draft) => Crane.At(draft.Pier!, draft.Slot!).Save();
}

/// <summary>
///     A failure the STORE raises, drawn under the field by a real form over the socket: a hand-wired host, a
///     table somebody else created, a tenant resolved from the request, and a page whose save only saves.
/// </summary>
/// <remarks>
///     Two ways the store refuses a duplicate, and the page must not be able to tell them apart: the declared
///     rule asked as a query when the table has no such index, and the database's own unique index when it has.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed partial class FormStoreFailureTests : IDisposable
{
    private const string MsSqlVariable = "RASK_MSSQL_TEST_DB";
    private const string MsSqlSkip = "Needs a SQL Server: set RASK_MSSQL_TEST_DB.";
    private const string MsSqlDatabase = "rask_form_failures";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-form-failure-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_taken_name_is_told_under_its_field_and_a_correction_lets_the_next_save_through(bool indexPresent)
    {
        using var host = await StartAsync<WharfFormApp>(Sqlite(), indexPresent);

        await TakenNameScenarioAsync(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task On_sql_server_a_taken_name_is_told_under_its_field_the_same_way(bool indexPresent)
    {
        Assert.SkipUnless(MsSql() is not null, MsSqlSkip);
        using var host = await StartAsync<WharfFormApp>(MsSql()!, indexPresent);

        try
        {
            await TakenNameScenarioAsync(host);
        }
        finally
        {
            await DropAsync(host);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rule_over_two_bound_fields_is_told_under_both(bool indexPresent)
    {
        using var host = await StartAsync<CraneFormApp>(Sqlite(), indexPresent);
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        page = await TypeAsync(ws, page, "pier", "North");
        page = await TypeAsync(ws, page, "slot", "3");
        var refused = await SubmitAsync(ws, page);

        Assert.Contains($"<p class=\"pier-error\">{Shown(Crane.PlaceTaken)}</p>", refused.Html, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"slot-error\">{Shown(Crane.PlaceTaken)}</p>", refused.Html, StringComparison.Ordinal);
        Assert.Contains("fault=none", refused.Html, StringComparison.Ordinal);
    }

    // Open the page as tenant 7, type a name tenant 7 already has, save; then correct it and save again.
    private static async Task TakenNameScenarioAsync(RaskTestHost host)
    {
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        page = await TypeAsync(ws, page, "name", "Budapest");
        var refused = await SubmitAsync(ws, page);
        var corrected = await TypeAsync(ws, refused.Html, "name", "Gyor");
        var saved = await SubmitAsync(ws, corrected);

        // Under THAT field, with the index's own message; no fault; nothing saved; the user stays where they are.
        Assert.Contains($"<p class=\"name-error\">{Shown(Wharf.NameTaken)}</p>", refused.Html, StringComparison.Ordinal);
        Assert.Contains("fault=none", refused.Html, StringComparison.Ordinal);
        Assert.Contains("saved=<", refused.Html, StringComparison.Ordinal);
        Assert.All(refused.Frames, frame => Assert.Null(LiveFrames.HistoryUrl(frame)));

        // Editing the field takes the message off, and the next save goes through for this tenant.
        Assert.DoesNotContain("name-error", corrected, StringComparison.Ordinal);
        Assert.Contains("saved=Gyor<", saved.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("name-error", saved.Html, StringComparison.Ordinal);
        Assert.Equal(["Budapest|7", "Budapest|8", "Gyor|7"], await RowsAsync(host));
    }

    // The message as the page carries it: text is encoded on its way into markup, accents included.
    private static string Shown(string message) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message);

    private static async Task<string> OpenAsync(RaskTestHost host, int tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/start");
        request.Headers.Add(CurrentCustomer.Header, tenant.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    // The socket carries no tenant: the session keeps the one it was opened with.
    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string page)
    {
        var sessionId = MarkupAssert.SessionId(page);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        return ws;
    }

    // What a browser sends for a field ahead of the next action, and the page once the server has finished with it.
    private static async Task<string> TypeAsync(WebSocket ws, string page, string id, string value)
    {
        var handler = ChangeHandler().Match(page[page.IndexOf($"<input id=\"{id}\"", StringComparison.Ordinal)..]).Groups[1].Value;
        await ws.SendJsonAsync(new { id = handler, type = "change", value }, ct: TestContext.Current.CancellationToken);

        return LastHtml(await ws.SettledAsync()) ?? page;
    }

    private static async Task<(string Html, List<string> Frames)> SubmitAsync(WebSocket ws, string page)
    {
        var handler = MarkupAssert.RequireAttr(page, "data-rask-on-submit");
        await ws.SendJsonAsync(new { id = handler, type = "submit", form = new { } }, ct: TestContext.Current.CancellationToken);

        var frames = await ws.SettledAsync();
        return (LastHtml(frames) ?? page, frames);
    }

    private static string? LastHtml(List<string> frames)
    {
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            using var frame = JsonDocument.Parse(frames[i]);
            if (frame.RootElement.TryGetProperty("html", out var html))
            {
                return html.GetString();
            }
        }

        return null;
    }

    private static async Task<List<string>> RowsAsync(RaskTestHost host)
    {
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyHarbourContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var rows = await legacy.Wharves.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        return [.. rows.Select(w => $"{w.Name}|{w.TenantId}").Order(StringComparer.Ordinal)];
    }

    private Action<DbContextOptionsBuilder> Sqlite() => o => o.UseSqlite($"Data Source={_dbPath};Pooling=False");

    private static Action<DbContextOptionsBuilder>? MsSql() =>
        Environment.GetEnvironmentVariable(MsSqlVariable) is { Length: > 0 } server
            ? o => o.UseSqlServer(new SqlConnectionStringBuilder(server) { InitialCatalog = MsSqlDatabase }.ConnectionString)
            : null;

    // With the index DROPPED the host is wired the documented way, and only the declared rule can refuse a
    // duplicate. With the index PRESENT the same registrations are made WITHOUT the declared-rule check, so the
    // refusal has to come from the database's own index and be translated.
    private static async Task<RaskTestHost> StartAsync<TApp>(Action<DbContextOptionsBuilder> database, bool indexPresent)
        where TApp : Component
    {
        var host = RaskTestHost.Create<TApp>(
            configureServices: services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<CurrentCustomer>();
                services.AddDbContextFactory<LegacyHarbourContext>(database);
                services.AddRaskTenant(sp => sp.GetRequiredService<CurrentCustomer>().TenantId);

                if (indexPresent)
                {
                    WireWithoutTheCheck(services, database);
                }
                else
                {
                    services.AddRaskData<HarbourDomainContext>(database);
                }
            },
            configureMiddleware: app => app.UseRaskData(),
            diffMode: LiveDiffMode.DisabledFull);

        // Which of the two refuses is decided by the wiring, so the wiring is checked rather than assumed.
        Assert.Equal(
            !indexPresent,
            host.Services.GetServices<ISaveChangesInterceptor>().Any(i => i.GetType().Name == "DeclaredRuleCheckInterceptor"));

        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyHarbourContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        legacy.AddRange(
            new LegacyWharf { Name = "Budapest", TenantId = 7 },
            new LegacyWharf { Name = "Budapest", TenantId = 8 },
            new LegacyCrane { Pier = "North", Slot = "3", TenantId = 7 });
        await legacy.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (!indexPresent)
        {
            var onTable = legacy.Database.IsSqlServer();
#pragma warning disable EF1002 // identifiers this test declared, not values
            await legacy.Database.ExecuteSqlRawAsync(
                onTable ? "DROP INDEX [IX_Wharves_Name_TenantId] ON [Wharves]" : "DROP INDEX \"IX_Wharves_Name_TenantId\"",
                TestContext.Current.CancellationToken);
            await legacy.Database.ExecuteSqlRawAsync(
                onTable ? "DROP INDEX [IX_Cranes_Pier_Slot_TenantId] ON [Cranes]" : "DROP INDEX \"IX_Cranes_Pier_Slot_TenantId\"",
                TestContext.Current.CancellationToken);
#pragma warning restore EF1002
        }

        return host;
    }

    // What AddRaskData<TContext>(o => …) registers, minus AddDeclaredRuleChecks.
    private static void WireWithoutTheCheck(IServiceCollection services, Action<DbContextOptionsBuilder> database)
    {
        services.AddRaskCqrs();
        services.AddRaskQuery();
        DataHostSeams.Add(services);
        services.AddRaskData<HarbourDomainContext>();
        services.AddDbContextFactory<HarbourDomainContext>((sp, o) =>
        {
            database(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        services.AddDbContextFactory<RaskReadDbContext>(database);
    }

    private static async Task DropAsync(RaskTestHost host)
    {
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyHarbourContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    [GeneratedRegex("data-rask-on-change=\"([^\"]+)\"")]
    private static partial Regex ChangeHandler();
}
