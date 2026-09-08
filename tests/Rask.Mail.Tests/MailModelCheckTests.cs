using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Mail.Tests;

/// <summary>
/// The boot-time guard for #1015, exercised through the wiring a real app actually uses.
/// </summary>
/// <remarks>
/// <para>
/// These tests register <c>AddRaskMail&lt;TContext&gt;</c> against a bare
/// <see cref="ServiceCollection" /> — no <c>RaskApp</c>, no meta package — because that is the shape
/// every scaffolded app has: <c>rask new</c> writes <c>builder.Services.AddRaskMail&lt;AppDbContext&gt;()</c>
/// into <c>Program.cs</c> directly, and references <c>Rask.Server</c> rather than the <c>Rask</c>
/// meta-package.
/// </para>
/// <para>
/// That distinction is the whole reason this file exists. The first attempt at the guard lived in the
/// meta package's battery wiring, where it passed its own tests and fired for nothing a real app does —
/// the repository's most expensive bug class, a gate that is green because it never runs. A test that
/// went through <c>RaskApp</c> could not tell the two apart; this one can.
/// </para>
/// </remarks>
[Collection("mail-db")]
public sealed class MailModelCheckTests
{
    /// <summary>A context that never maps the mail table — the mistake the issue is about.</summary>
    private sealed class UnmappedDbContext(DbContextOptions<UnmappedDbContext> options) : DbContext(options);

    /// <summary>A context that maps it, the way a correct app's does.</summary>
    private sealed class MappedDbContext(DbContextOptions<MappedDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskMail();
    }

    /// <summary>A path that is guaranteed not to exist, standing in for "migrations have not run".</summary>
    private static string MissingDbPath() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))}.db";

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    /// <summary>The mail battery's model check, found by name because the type is internal.</summary>
    private static List<IHostedService> ModelChecks(IServiceProvider provider) =>
        [.. provider.GetServices<IHostedService>()
            .Where(s => s.GetType().Name.StartsWith("MailModelCheck", StringComparison.Ordinal))];

    [Fact]
    public async Task Direct_wiring_registers_the_check_and_it_names_the_missing_line()
    {
        var services = Services();
        services.AddRaskMail<UnmappedDbContext>(o => o.From = "noreply@example.com");
        services.AddDbContextFactory<UnmappedDbContext>(o => o.UseSqlite(MissingDbPath()));

        using var provider = services.BuildServiceProvider();

        var check = Assert.Single(ModelChecks(provider));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => check.StartAsync(CancellationToken.None));

        // The line to type, not merely that something is wrong.
        Assert.Contains("modelBuilder.AddRaskMail();", error.Message, StringComparison.Ordinal);
        Assert.Contains("OnModelCreating", error.Message, StringComparison.Ordinal);

        // And the symptom it replaces, so a reader who searched for the old runtime error lands here.
        Assert.Contains("Cannot create a DbSet for 'QueuedMail'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mapped_app_whose_database_does_not_exist_yet_still_starts()
    {
        // The constraint that decides whether this may fail the boot at all. A freshly scaffolded app boots
        // before its first migration has run, so refusing to start over a missing TABLE would mean a new
        // app does not start. Reading the MODEL rather than the database separates the two: "not mapped"
        // is a code mistake and always wrong; "not migrated yet" is normal and none of this check's
        // business. This context maps the table and points at a file that does not exist.
        var services = Services();
        services.AddRaskMail<MappedDbContext>(o => o.From = "noreply@example.com");
        services.AddDbContextFactory<MappedDbContext>(o => o.UseSqlite(MissingDbPath()));

        using var provider = services.BuildServiceProvider();

        var check = Assert.Single(ModelChecks(provider));
        await check.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void A_repeated_registration_adds_only_one_check()
    {
        // AddRaskMail documents itself as idempotent. The check is registered as a TYPE rather than a
        // factory delegate precisely so AddHostedService's TryAddEnumerable can deduplicate it; a factory
        // would be added again on every call, and the app would fail the boot twice over.
        var services = Services();
        services.AddRaskMail<MappedDbContext>(o => o.From = "noreply@example.com");
        services.AddRaskMail<MappedDbContext>(o => o.From = "noreply@example.com");
        services.AddDbContextFactory<MappedDbContext>(o => o.UseSqlite(MissingDbPath()));

        using var provider = services.BuildServiceProvider();

        Assert.Single(ModelChecks(provider));
    }
}
