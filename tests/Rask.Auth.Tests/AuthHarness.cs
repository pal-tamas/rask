using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Mail;

namespace Rask.Auth.Tests;

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

    /// <summary>The last message sent to an address, or null when there is none.</summary>
    public SentMail? LastTo(string address) => _sent.FindLast(m => m.To == address);

    public Task SendAsync(Email email, CancellationToken cancellationToken = default)
    {
        // Email.Body(Component) has already rendered to HTML by the time it is queued, so this is the
        // same string a real send would hand to the SMTP transport.
        _sent.Add(new SentMail(
            email.ToRecipients[0].Address,
            email.SubjectText ?? "",
            email.HtmlBody ?? ""));

        return Task.CompletedTask;
    }

    public Task ScheduleAsync(Email email, TimeSpan delay, CancellationToken cancellationToken = default) =>
        SendAsync(email, cancellationToken);

    public Task ScheduleAsync(Email email, DateTimeOffset runAt, CancellationToken cancellationToken = default) =>
        SendAsync(email, cancellationToken);
}

/// <summary>The application context an app would write, with the auth tables mapped onto it.</summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskAuth();
}

/// <summary>
///     A real-SQLite service provider wired the way the auth battery wires a scaffolded app.
/// </summary>
/// <remarks>
///     Deliberately a file rather than <c>:memory:</c>: the concurrency tests need several connections
///     to see one database, which an in-memory database only does through a shared cache and a kept-open
///     connection. A file is what a real app uses, and it is what makes a contended write actually contend.
/// </remarks>
public sealed class AuthHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly bool _ownsFile;

    /// <param name="configure">Deviations from the default options.</param>
    /// <param name="dbPath">
    ///     An existing database to reopen. Passing the <see cref="DbPath" /> of another harness is how a
    ///     test restarts the same app: the file is the only thing a restart actually keeps.
    /// </param>
    /// <param name="mail">
    ///     Whether to register a mail battery. <b>Off by default, deliberately</b> — it is the state of a
    ///     scaffolded app that has not configured one, and the flows have to behave sanely there too.
    ///     Passing <c>true</c> registers <see cref="MailSpy" />, which captures what would have been sent.
    /// </param>
    public AuthHarness(Action<AuthOptions>? configure = null, string? dbPath = null, bool mail = false)
    {
        _ownsFile = dbPath is null;
        DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"rask-auth-test-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));
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

        _provider = services.BuildServiceProvider();

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    /// <summary>The token the harness configures, so a test can present the right one.</summary>
    public const string FirstRunTokenValue = "test-first-run-token";

    /// <summary>The origin emailed links are built against.</summary>
    public const string Origin = "https://rask.test";

    public string DbPath { get; }

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

    /// <summary>The roles held by the account with this email, lowercased.</summary>
    public async Task<IReadOnlyList<string>> RolesOfAsync(string email)
    {
        using var scope = NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<RaskUser>>();
        var user = await users.FindByEmailAsync(email);

        return user is null ? [] : (await users.GetRolesAsync(user)).ToArray();
    }

    public async Task<int> UserCountAsync()
    {
        await using var db = NewContext();
        return await db.Set<RaskUser>().CountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        // The pooled connections keep a handle on the file, so the delete below fails without this.
        // Rask has hit this before as an intermittent "database is locked" in unrelated suites.
        SqliteConnection.ClearAllPools();

        if (!_ownsFile)
        {
            // A reopened database belongs to the harness that made it.
            return;
        }

        try
        {
            File.Delete(DbPath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }
}
