using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Data;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

/// <summary>A table that already exists, mapped as it is: an identity key and a tenant numbered by an int.</summary>
public sealed class Terminal : Aggregate<int>
{
    private Terminal() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public static Terminal Named(string name) => new() { Name = name };

    public static void Configure(EntityTypeBuilder<Terminal> builder)
    {
        builder.ToTable("Terminals");
        builder.HasIndex(t => new { t.Name, t.TenantId }).IsUnique();
    }
}

/// <summary>The class and the context the app already had. They own the schema.</summary>
public sealed class LegacyTerminal
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int? TenantId { get; set; }
}

public sealed class LegacyTerminalContext(DbContextOptions<LegacyTerminalContext> options) : DbContext(options)
{
    public DbSet<LegacyTerminal> Terminals => Set<LegacyTerminal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<LegacyTerminal>(b =>
        {
            b.ToTable("Terminals");
            b.HasIndex(t => new { t.Name, t.TenantId }).IsUnique();
        });
}

/// <summary>The context the app adds beside it: Rask's model over the same tables.</summary>
public sealed class TerminalDomainContext(DbContextOptions<TerminalDomainContext> options) : RaskDbContext(options);

/// <summary>
///     What the app already has for "which customer is this request for": a singleton over
///     <see cref="IHttpContextAccessor" />. A header stands in for the host name.
/// </summary>
public sealed class CurrentCustomer(IHttpContextAccessor http)
{
    public const string Header = "X-Tenant";

    public int? TenantId =>
        int.TryParse(http.HttpContext?.Request.Headers[Header], out var tenant) ? tenant : null;
}

/// <summary>A live page: who and which tenant on its first render, and its tenant's rows when asked.</summary>
public sealed partial class TerminalsApp : Component
{
    private List<string>? _names;

    protected override Component? Render() =>
    [
        Markup.P[$"tenant={Current.Tenant?.ToString() ?? "none"}"],
        Markup.P[$"user={Current.UserId?.ToString() ?? "nobody"}"],
        Markup.P[$"names={(_names is null ? "unread" : string.Join(',', _names))}"],
        Button.OnClick(Load)["load"]
    ];

    private async Task Load() => _names = await Terminal.OrderBy(t => t.Name).Select(t => t.Name);
}

/// <summary>
///     A host wired by hand — <c>AddRask()</c> and <c>MapRask&lt;TApp&gt;()</c>, not <c>RaskApp</c> — gets Rask.Data in
///     two calls: <c>AddRaskData&lt;TContext&gt;(o =&gt; …)</c> and <c>app.UseRaskData()</c>.
/// </summary>
/// <remarks>
///     The app's own context created the table and wrote every row in it; Rask's context only maps it. Rows for
///     two tenants and one for nobody are there in every test, so a read that filtered by nothing would show.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class HandWiredDataHostTests : IDisposable
{
    private const string UserHeader = "X-User";
    private static readonly Guid Alice = Guid.NewGuid();

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-hand-wired-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task An_http_endpoint_reads_only_the_rows_of_the_tenant_the_resolver_named()
    {
        using var host = await StartAsync();

        var acme = await GetAsync(host, "/api/terminals", tenant: 7);
        var globex = await GetAsync(host, "/api/terminals", tenant: 8);

        Assert.Equal("Budapest,Szeged", acme);
        Assert.Equal("Vienna", globex);
    }

    [Fact]
    public async Task An_http_request_with_no_tenant_reads_nothing()
    {
        using var host = await StartAsync();

        var names = await GetAsync(host, "/api/terminals", tenant: null);

        Assert.Equal("", names);
    }

    [Fact]
    public async Task A_live_sessions_work_reads_its_tenants_rows_long_after_the_request_that_opened_it()
    {
        using var host = await StartAsync();
        var opened = await GetAsync(host, "/start", tenant: 7, user: Alice);
        var sessionId = MarkupAssert.SessionId(opened);

        // The socket carries the same user, as a browser's cookie would, and no tenant header at all: what the
        // session was told when it opened is what it keeps.
        host.WebSockets.ConfigureRequest = request => request.Headers[UserHeader] = Alice.ToString();
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        await ws.SendJsonAsync(new { id = MarkupAssert.FirstHandlerId(opened) }, ct: TestContext.Current.CancellationToken);
        var after = await ws.ReceiveUntilAsync(
            frame => !frame.Contains("names=unread", StringComparison.Ordinal), "the render after the rows were read");

        Assert.Contains("tenant=00000000-0000-0000-0000-000000000007", opened, StringComparison.Ordinal);
        Assert.Contains($"user={Alice}", opened, StringComparison.Ordinal);
        using var frame = JsonDocument.Parse(after);
        var html = frame.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("names=Budapest,Szeged<", html, StringComparison.Ordinal);
        Assert.Contains("tenant=00000000-0000-0000-0000-000000000007", html, StringComparison.Ordinal);
        Assert.Contains($"user={Alice}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_session_opened_with_no_tenant_reads_nothing()
    {
        using var host = await StartAsync();
        var opened = await GetAsync(host, "/start", tenant: null);
        var sessionId = MarkupAssert.SessionId(opened);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        await ws.SendJsonAsync(new { id = MarkupAssert.FirstHandlerId(opened) }, ct: TestContext.Current.CancellationToken);
        var after = await ws.ReceiveUntilAsync(
            frame => !frame.Contains("names=unread", StringComparison.Ordinal), "the render after the rows were read");

        Assert.Contains("tenant=none", opened, StringComparison.Ordinal);
        using var frame = JsonDocument.Parse(after);
        Assert.Contains("names=<", frame.RootElement.GetProperty("html").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_from_an_endpoint_is_stamped_and_nothing_but_the_legacy_table_exists_afterwards()
    {
        using var host = await StartAsync();

        var saved = await GetAsync(host, "/api/terminals/add?name=Gyor", tenant: 8);

        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyTerminalContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var row = await legacy.Terminals.SingleAsync(t => t.Name == "Gyor", TestContext.Current.CancellationToken);
        var tables = await legacy.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal("saved", saved);
        Assert.Equal(8, row.TenantId);
        Assert.Equal(["Terminals"], tables);
    }

    [Fact]
    public void UseRaskData_without_AddRaskData_says_which_call_is_missing()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();

        var refused = Assert.Throws<InvalidOperationException>(() => app.UseRaskData());

        Assert.Contains("AddRaskData<AppDbContext>(o =>", refused.Message, StringComparison.Ordinal);
    }

    // The whole of what a hand-wired host writes for data is the two marked calls.
    private async Task<RaskTestHost> StartAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";

        var host = RaskTestHost.Create<TerminalsApp>(
            configureServices: services =>
            {
                // What the app had before.
                services.AddHttpContextAccessor();
                services.AddSingleton<CurrentCustomer>();
                services.AddDbContextFactory<LegacyTerminalContext>(o => o.UseSqlite(connection));

                // What it adds.
                services.AddRaskData<TerminalDomainContext>(o => o.UseSqlite(connection));
                services.AddRaskTenant(sp => sp.GetRequiredService<CurrentCustomer>().TenantId);
            },
            configureMiddleware: app =>
            {
                // Stands in for UseAuthentication: the principal arrives on HttpContext.User.
                app.Use(static (context, next) =>
                {
                    if (Guid.TryParse(context.Request.Headers[UserHeader], out var user))
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, user.ToString())], "Test"));
                    }

                    return next(context);
                });

                app.UseRaskData();

                app.Map("/api/terminals/add", add => add.Run(static async context =>
                {
                    await Terminal.Named(context.Request.Query["name"]!).Save();
                    await context.Response.WriteAsync("saved");
                }));
                app.Map("/api/terminals", list => list.Run(static async context =>
                    await context.Response.WriteAsync(string.Join(',', await Terminal.OrderBy(t => t.Name).Select(t => t.Name)))));
            },
            diffMode: LiveDiffMode.DisabledFull);

        // The legacy context owns the schema and wrote every row. Rask's is never asked to create anything.
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyTerminalContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        legacy.AddRange(
            new LegacyTerminal { Name = "Budapest", TenantId = 7 },
            new LegacyTerminal { Name = "Szeged", TenantId = 7 },
            new LegacyTerminal { Name = "Vienna", TenantId = 8 },
            new LegacyTerminal { Name = "Nowhere", TenantId = null });
        await legacy.SaveChangesAsync(TestContext.Current.CancellationToken);

        return host;
    }

    private static async Task<string> GetAsync(RaskTestHost host, string path, int? tenant, Guid? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (tenant is { } number)
        {
            request.Headers.Add(CurrentCustomer.Header, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (user is { } id)
        {
            request.Headers.Add(UserHeader, id.ToString());
        }

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
