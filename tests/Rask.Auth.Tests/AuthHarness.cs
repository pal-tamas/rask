using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Data;
using Rask.Mailing;
using Rask.Tests.Shared;

namespace Rask.Auth.Tests;

/// <summary>
///     The account type these tests run on. Rask ships none — an app declares its own and the generator
///     finds it — so the suite declares one exactly as a scaffolded app does.
/// </summary>
public sealed class TestUser : Authenticatable
{
    public string DisplayName { get; private set; } = "";

    public void Rename(string name) => DisplayName = name;

    public static TestUser Blank() => new();

    /// <summary>The type of the one claim this user gives for itself; empty gives none.</summary>
    public string BadgeType { get; private set; } = "";

    public string Badge { get; private set; } = "";

    public void Wear(string type, string value) => (BadgeType, Badge) = (type, value);

    protected override IEnumerable<System.Security.Claims.Claim> Claims() =>
        BadgeType.Length == 0 ? [] : [new(BadgeType, Badge)];
}

/// <summary>One captured message, in the terms a test asks questions in.</summary>
/// <param name="To">The recipient.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Html">The rendered body.</param>
public sealed record SentMail(string To, string Subject, string Html)
{
    /// <summary>The first <c>href</c> in the body — the link the message exists to deliver.</summary>
    /// <remarks>
    ///     Read out of the rendered HTML rather than taken from the code that built it. A test that asked
    ///     the builder would still pass if the link never reached the markup, which is the failure that
    ///     actually matters: an email that arrives with no way to act on it.
    /// </remarks>
    public string? Link =>
        System.Text.RegularExpressions.Regex.Match(Html, "href=\"([^\"]+)\"") is { Success: true } match
            ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)
            : null;
}

/// <summary>A mail battery that keeps every message instead of sending it.</summary>
public sealed class MailSpy : IMail
{
    private readonly List<SentMail> _sent = [];

    public IReadOnlyList<SentMail> Sent => _sent;

    /// <summary>
    /// Makes every send throw, which is the shape a misconfigured mail battery has: registered, so
    /// <c>IsConfigured</c> is true, but unable to queue — most often because its tables are not in the
    /// DbContext model. It is the only state in which #1011's membership oracle was reachable.
    /// </summary>
    public bool Throws { get; set; }

    /// <summary>The last message sent to an address, or null when there is none.</summary>
    public SentMail? LastTo(string address) => _sent.FindLast(m => m.To == address);

    public Task Add(Mail.Message email, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken = default)
    {
        if (Throws)
        {
            throw new InvalidOperationException("mail battery is misconfigured");
        }

        // Mail.Message.Body(Component) has already rendered to HTML by the time it is queued, so this is the
        // same string a real send would hand to the SMTP transport.
        _sent.Add(new SentMail(
            email.ToRecipients[0].Address,
            email.SubjectText ?? "",
            email.HtmlBody ?? ""));

        return Task.CompletedTask;
    }
}

/// <summary>The application context an app would write, with the auth tables mapped onto it.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddRaskAuth();

        // Last, as in a scaffolded app: the soft-delete filter that hides an ended session, the concurrency token.
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>
///     A real-SQLite service provider wired the way the auth battery wires a scaffolded app.
/// </summary>
/// <remarks>
///     The database is in memory and the harness's own (<see cref="InMemorySqlite" />): a shared cache that every
///     connection the harness opens sees, so the concurrency tests still have several connections contending for one
///     database, and test classes run side by side with nothing on disk between them.
/// </remarks>
public sealed class AuthHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly InMemorySqlite? _owned;

    /// <param name="configure">Deviations from the default options.</param>
    /// <param name="reopen">
    ///     Another harness whose database this one opens. It is how a test restarts the same app: a new provider —
    ///     new singletons, a new startup — over the rows the first one left, which are the only thing a restart keeps.
    ///     The database stays the first harness's, and lives as long as it does.
    /// </param>
    /// <param name="mail">
    ///     Whether to register a mail battery. <b>Off by default, deliberately</b> — it is the state of a
    ///     scaffolded app that has not configured one, and the flows have to behave sanely there too.
    ///     Passing <c>true</c> registers <see cref="MailSpy" />, which captures what would have been sent.
    /// </param>
    /// <param name="clock">The clock sessions and tokens read, when a test needs to move time.</param>
    /// <param name="extraServices">Registrations a test adds last, such as a host environment.</param>
    public AuthHarness(
        Action<AuthOptions>? configure = null,
        AuthHarness? reopen = null,
        bool mail = false,
        TimeProvider? clock = null,
        Action<IServiceCollection>? extraServices = null)
    {
        _owned = reopen is null ? new InMemorySqlite() : null;
        ConnectionString = reopen?.ConnectionString ?? _owned!.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        // The interceptors a scaffolded app runs, so an ended session is soft-deleted exactly as it is there. Domain
        // events are not dispatched: nothing here has a dispatcher, and no test asserts on delivery.
        services.AddRaskData();

        // Pooling off, which the in-memory database's connection string carries (#1087): a pooled connection's return
        // runs SqliteConnection.Deactivate, which un-registers the EF_DECIMAL collation EF Core adds — and SQLite refuses
        // that while a collected-but-unfinalized statement is still active, so the many-simultaneous-claims shape failed
        // unrelated gates with "unable to delete/modify collation sequence due to active statements".
        services.AddDbContextFactory<AuthDbContext>((sp, o) => o
            .UseSqlite(ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        // A real hash at a fraction of the work: 600,000 iterations per registration would make this suite
        // measure PBKDF2 rather than the flows. Registered first, so AddRaskAuth's TryAdd keeps it.
        services.AddSingleton(new PasswordHasher(iterations: 1_000));
        services.AddRaskAuth<AuthDbContext>(o =>
        {
            // A fixed token keeps the tests from having to read it back out of the log.
            o.FirstRunToken = FirstRunTokenValue;

            // There is no request and no listening server here, so an emailed link would have nothing
            // absolute to build on. A configured origin is also what a real deployment behind a proxy
            // sets, so this is the shape under test rather than a convenience for it.
            o.PublicOrigin = Origin;

            configure?.Invoke(o);
        });

        if (mail)
        {
            Mail = new MailSpy();
            services.AddSingleton<IMail>(Mail);
        }

        extraServices?.Invoke(services);

        _provider = services.BuildServiceProvider();

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    /// <summary>The token the harness configures, so a test can present the right one.</summary>
    public const string FirstRunTokenValue = "test-first-run-token";

    /// <summary>The origin emailed links are built against.</summary>
    public const string Origin = "https://rask.test";

    /// <summary>The connection string of the database this harness runs on.</summary>
    public string ConnectionString { get; }

    /// <summary>What the app would have sent, when the harness was built with a mail battery.</summary>
    public MailSpy? Mail { get; }

    public IServiceProvider Services => _provider;

    public AuthDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContext();

    /// <summary>Runs the startup step that decides whether a first-run token is pending.</summary>
    public async Task StartAsync()
    {
        foreach (var hosted in _provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    /// <summary>A fresh DI scope, as a request or a live session would have.</summary>
    public IServiceScope NewScope() => _provider.CreateScope();

    public FirstRunToken Token => _provider.GetRequiredService<FirstRunToken>();

    /// <summary>The roles held by the account with this email.</summary>
    public async Task<IReadOnlyList<string>> RolesOfAsync(string email)
    {
        var user = await UserAsync(email);
        return user is null ? [] : user.Roles;
    }

    /// <summary>The user with this email, as it is stored now.</summary>
    public async Task<TestUser?> UserAsync(string email)
    {
        await using var db = NewContext();
        var normalized = Authenticatable.NormalizeEmail(email);
        return await db.Set<TestUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalized);
    }

    public async Task<int> UserCountAsync()
    {
        await using var db = NewContext();
        return await db.Set<TestUser>().CountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        // A reopened database belongs to the harness that made it.
        if (_owned is not null)
        {
            await _owned.DisposeAsync();
        }
    }
}
