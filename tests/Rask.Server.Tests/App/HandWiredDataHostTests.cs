using Microsoft.Extensions.Logging;
using Rask.Querying;
using Rask.Core.Routing;
using Rask.Core.Authentication;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Cqrs;
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

    public const string NameTaken = "A terminal with this name already exists.";

    public static Terminal Named(string name) => new() { Name = name };

    public static void Configure(EntityTypeBuilder<Terminal> builder)
    {
        builder.ToTable("Terminals");
        builder.HasIndex(t => new { t.Name, t.TenantId }).IsUnique(NameTaken);
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
    public async Task A_live_sessions_work_reads_the_rows_of_the_tenant_its_connection_names()
    {
        using var host = await StartAsync();
        var opened = await GetAsync(host, "/start", tenant: 7, user: Alice);
        var sessionId = MarkupAssert.SessionId(opened);

        // The socket carries the same user and the same tenant, as a browser's cookie and its host name would:
        // each event asks the resolver again, and it reads the connection's request.
        host.WebSockets.ConfigureRequest = request =>
        {
            request.Headers[UserHeader] = Alice.ToString();
            request.Headers[CurrentCustomer.Header] = "7";
        };
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

    [Theory]
    [InlineData(TenantFrom.ThePrincipal)]
    [InlineData(TenantFrom.TheRaskClaim)]
    public async Task A_tab_whose_user_chose_another_tenant_elsewhere_reads_that_tenant_s_rows_at_its_next_action(TenantFrom tenantFrom)
    {
        using var host = await StartAsync(tenantFrom);
        var chosen = host.Services.GetRequiredService<ChosenPartner>();
        chosen.Kind = tenantFrom;
        var opened = await GetAsync(host, "/start", tenant: null, user: Alice, partner: 7);
        using var ws = await AttachAsync(host, MarkupAssert.SessionId(opened), partner: 7);
        var before = await LoadAsync(ws, opened, "Budapest,Szeged");

        chosen.Now = 8;
        var after = await LoadAsync(ws, opened, "Vienna");

        Assert.Contains("tenant=00000000-0000-0000-0000-000000000007", before, StringComparison.Ordinal);
        Assert.Contains("tenant=00000000-0000-0000-0000-000000000008", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reconnect_that_carries_a_token_for_another_tenant_works_in_that_tenant()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        var opened = await GetAsync(host, "/start", tenant: null, user: Alice, partner: 7);
        var sessionId = MarkupAssert.SessionId(opened);
        using (var first = await AttachAsync(host, sessionId, partner: 7))
        {
            await LoadAsync(first, opened, "Budapest,Szeged");
            await first.CloseAndAwaitServerCleanupAsync();
        }

        using var second = await AttachAsync(host, sessionId, partner: 8);
        var after = await LoadAsync(second, opened, "Vienna");

        Assert.Contains("tenant=00000000-0000-0000-0000-000000000008", after, StringComparison.Ordinal);
    }

    // The socket's request is "/rask/ws" whatever the page: the page's address is the session's RouteState.
    [Fact]
    public async Task A_tenant_read_from_the_page_s_address_is_the_session_s_and_follows_it_as_it_navigates()
    {
        using var host = await StartAsync(TenantFrom.ThePagesAddress);
        var opened = await GetAsync(host, "/t/7/terminals", tenant: null, user: Alice);
        using var ws = await AttachAsync(host, MarkupAssert.SessionId(opened), partner: null);
        var before = await LoadAsync(ws, opened, "Budapest,Szeged");

        await ws.SendJsonAsync(new { type = "navigate", path = "/t/8/terminals" }, ct: TestContext.Current.CancellationToken);
        var after = await LoadAsync(ws, opened, "Vienna");

        Assert.Contains("tenant=00000000-0000-0000-0000-000000000007", before, StringComparison.Ordinal);
        Assert.Contains("tenant=00000000-0000-0000-0000-000000000008", after, StringComparison.Ordinal);
    }

    // A header the page's request carried and its socket does not. Reading nothing would look like an empty table.
    [Fact]
    public async Task A_resolver_that_read_what_only_the_page_s_request_carried_is_told_so_at_the_first_read_of_an_event()
    {
        var log = new CapturedLog();
        using var host = await StartAsync(TenantFrom.TheRequestsHeader, log);
        var opened = await GetAsync(host, "/start", tenant: 7, user: Alice);
        using var ws = await AttachAsync(host, MarkupAssert.SessionId(opened), partner: null);

        await ws.SendJsonAsync(new { id = MarkupAssert.FirstHandlerId(opened) }, ct: TestContext.Current.CancellationToken);
        await WaitFor.True(() => log.Entries.Any(static e => e.Exception is MissingTenantException), "the read is refused");

        var refused = log.Entries.First(static e => e.Exception is MissingTenantException).Exception!;
        Assert.Contains("AddRaskTenant", refused.Message, StringComparison.Ordinal);
        Assert.Contains("only the page's own request carried", refused.Message, StringComparison.Ordinal);
        Assert.Contains("RouteState", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Current.Principal", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_the_session_s_queries_read_for_one_tenant_is_dropped_when_its_user_chooses_another()
    {
        using var host = await StartAsync(TenantFrom.ThePrincipal);
        var chosen = host.Services.GetRequiredService<ChosenPartner>();
        chosen.Kind = TenantFrom.ThePrincipal;
        var opened = await GetAsync(host, "/start", tenant: null, user: Alice, partner: 7);
        var sessionId = MarkupAssert.SessionId(opened);
        using var ws = await AttachAsync(host, sessionId, partner: 7);
        var queries = (SessionQueryClient)host.Store.Peek(sessionId)!.Services.GetRequiredService<IQueryClient>();
        queries.Set(QueryKey.Of("terminals"), "Budapest,Szeged");

        chosen.Now = 8;
        await LoadAsync(ws, opened, "Vienna");

        Assert.False(queries.TryGetData<string>(QueryKey.Of("terminals"), out _));
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
    public async Task A_declared_unique_rule_refuses_a_duplicate_even_when_the_table_has_no_such_index()
    {
        using var host = await StartAsync();
        await using (var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyTerminalContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            // The table somebody else created never got the index the aggregate declares.
            await legacy.Database.ExecuteSqlAsync(
                $"DROP INDEX \"IX_Terminals_Name_TenantId\"", TestContext.Current.CancellationToken);
        }

        var sameTenant = await GetAsync(host, "/api/terminals/add?name=Budapest", tenant: 7);
        var otherTenant = await GetAsync(host, "/api/terminals/add?name=Budapest", tenant: 8);

        // No inner exception: the database had nothing to refuse it with. The check did.
        Assert.Equal($"refused|{Terminal.NameTaken}|Name|True", sameTenant);
        Assert.Equal("saved", otherTenant);
    }

    [Fact]
    public async Task The_check_runs_after_the_tenant_is_stamped_and_before_a_violation_is_translated()
    {
        using var host = await StartAsync();

        var order = host.Services.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor>()
            .Select(interceptor => interceptor.GetType().Name)
            .ToList();

        Assert.True(order.IndexOf("AuditingInterceptor") < order.IndexOf("DeclaredRuleCheckInterceptor"));
        Assert.True(order.IndexOf("DeclaredRuleCheckInterceptor") < order.IndexOf("ViolationMessageInterceptor"));
        Assert.Equal("ViolationMessageInterceptor", order[^1]);
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
    private async Task<RaskTestHost> StartAsync(TenantFrom tenantFrom = TenantFrom.TheRequestsHeader, CapturedLog? log = null)
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
                services.AddSingleton<ChosenPartner>();
                services.AddSingleton<ISessionRevalidator, PartnerRevalidator>();
                services.AddScoped<IQueryClient>(static _ => new SessionQueryClient(new NothingToAsk()));
                if (log is not null)
                {
                    services.AddLogging(logging => logging.AddProvider(log));
                }

                switch (tenantFrom)
                {
                    case TenantFrom.TheRequestsHeader:
                        services.AddRaskTenant(sp => sp.GetRequiredService<CurrentCustomer>().TenantId);
                        break;
                    case TenantFrom.ThePrincipal:
                        services.AddRaskTenant(static _ => PartnerOf(Current.Principal));
                        break;
                    case TenantFrom.ThePagesAddress:
                        services.AddRaskTenant(static sp => PartnerIn(sp.GetRequiredService<RouteState>().Path));
                        break;
                    default:
                        // No resolver: the principal's own rask:tenant claim says.
                        break;
                }
            },
            configureMiddleware: app =>
            {
                // Stands in for UseAuthentication: the principal arrives on HttpContext.User, with the partner
                // its token names.
                app.Use((context, next) =>
                {
                    if (Guid.TryParse(context.Request.Headers[UserHeader], out var user))
                    {
                        var partner = int.TryParse(context.Request.Headers[PartnerHeader], out var named) ? named : (int?)null;
                        context.User = Principal(user, partner, tenantFrom);
                    }

                    return next(context);
                });

                app.UseRaskData();

                app.Map("/api/terminals/add", add => add.Run(static async context =>
                {
                    try
                    {
                        await Terminal.Named(context.Request.Query["name"]!).Save();
                        await context.Response.WriteAsync("saved");
                    }
                    catch (RaskValidationException refused)
                    {
                        var failure = refused.Failures.Single();
                        await context.Response.WriteAsync(
                            $"refused|{failure.Message}|{string.Join(',', failure.Fields)}|{refused.InnerException is null}");
                    }
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

    private static async Task<string> GetAsync(RaskTestHost host, string path, int? tenant, Guid? user = null, int? partner = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (partner is { } chosen)
        {
            request.Headers.Add(PartnerHeader, chosen.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

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

    // ── a live page follows its user's tenant ────────────────────────────────────────────────────

    private const string PartnerHeader = "X-Partner";
    private const string PartnerClaim = "partner";

    /// <summary>Where the app says a request's tenant comes from.</summary>
    public enum TenantFrom
    {
        /// <summary>A header of the request: the host name's stand-in.</summary>
        TheRequestsHeader,

        /// <summary>A claim of the signed-in user, read by the app's resolver.</summary>
        ThePrincipal,

        /// <summary>The page's address: <c>/t/7/…</c>.</summary>
        ThePagesAddress,

        /// <summary>The principal's own <c>rask:tenant</c> claim, with no resolver registered.</summary>
        TheRaskClaim,
    }

    private static ClaimsPrincipal Principal(Guid user, int? partner, TenantFrom tenantFrom)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "Test");
        if (partner is { } number)
        {
            identity.AddClaim(tenantFrom == TenantFrom.TheRaskClaim
                ? new Claim(Tenant.ClaimType, new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)number).ToString())
                : new Claim(PartnerClaim, number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new ClaimsPrincipal(identity);
    }

    private static int? PartnerOf(ClaimsPrincipal? principal) =>
        int.TryParse(principal?.FindFirst(PartnerClaim)?.Value, out var partner) ? partner : null;

    // "/t/7/terminals" is tenant 7's page.
    private static int? PartnerIn(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries) is ["t", var number, ..] && int.TryParse(number, out var partner)
            ? partner
            : null;

    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string sessionId, int? partner)
    {
        host.WebSockets.ConfigureRequest = request =>
        {
            request.Headers[UserHeader] = Alice.ToString();
            if (partner is { } chosen)
            {
                request.Headers[PartnerHeader] = chosen.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        };
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        return ws;
    }

    // Presses "load" and returns the page once it shows the names it read.
    private static async Task<string> LoadAsync(WebSocket ws, string opened, string names)
    {
        await ws.SendJsonAsync(new { id = MarkupAssert.FirstHandlerId(opened) }, ct: TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveUntilAsync(
            f => f.Contains($"names={names}<", StringComparison.Ordinal), $"the render that shows {names}");
        using var json = JsonDocument.Parse(frame);
        return json.RootElement.GetProperty("html").GetString()!;
    }

    // The partner a user chose somewhere else, as the app's own store has it.
    private sealed class ChosenPartner
    {
        public int? Now { get; set; }

        public TenantFrom Kind { get; set; }
    }

    // Hands back the principal as the store has it now: the same instance until the user chooses another partner.
    private sealed class PartnerRevalidator(ChosenPartner chosen) : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(chosen.Now is { } partner
                ? Principal(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), partner, chosen.Kind)
                : principal);
    }

    // A query cache needs a dispatcher. Nothing here dispatches.
    private sealed class NothingToAsk : IDispatcher
    {
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TEvent>(TEvent e, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();

        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(
            ISubscription<TEvent> subscription, CancellationToken cancellationToken = default)
            where TEvent : IEvent => throw new NotSupportedException();
    }
}
