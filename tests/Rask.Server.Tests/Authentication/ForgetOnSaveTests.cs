using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     <c>.ForgetOnSave&lt;T&gt;()</c>: a user kept by a session revalidator is loaded afresh the moment a save of the
///     named type commits — here a save from no session at all, as a background job's would be, on SQLite.
/// </summary>
public sealed class ForgetOnSaveTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-forget-on-save-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Saving_the_user_drops_the_kept_user_at_once()
    {
        await using var services = await StartAsync();
        var revalidator = services.GetRequiredService<ISessionRevalidator>();
        var check = services.GetRequiredService<AccountRowCheck>();
        var alice = Alice();
        var stands = await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);

        await using (var db = await services.GetRequiredService<IDbContextFactory<AccountsContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            (await db.Accounts.SingleAsync(a => a.Name == "alice", TestContext.Current.CancellationToken)).Disabled = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var afterSave = await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);

        Assert.Same(alice, stands);
        Assert.Null(afterSave);
        Assert.Equal(2, check.Loads);
    }

    [Fact]
    public async Task A_save_of_another_type_keeps_the_kept_user()
    {
        await using var services = await StartAsync();
        var revalidator = services.GetRequiredService<ISessionRevalidator>();
        var check = services.GetRequiredService<AccountRowCheck>();
        var alice = Alice();
        await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);

        await using (var db = await services.GetRequiredService<IDbContextFactory<AccountsContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.Notes.Add(new NoteRow { Text = "hello" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var again = await revalidator.Revalidate(alice, TestContext.Current.CancellationToken);

        Assert.Same(alice, again);
        Assert.Equal(1, check.Loads);
    }

    private static ClaimsPrincipal Alice() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "TestCookie"));

    // The app's container as far as this needs: Rask.Data's interceptors, the revalidator, and a context on SQLite
    // that the revalidator itself reads through — the interceptor it is told by is built for that very factory.
    private async Task<ServiceProvider> StartAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddSessionRevalidator<AccountRowCheck>().Keep(1.Hour).ForgetOnSave<AccountRow>();
        services.AddDbContextFactory<AccountsContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        var provider = services.BuildServiceProvider();

        await using var db = await provider.GetRequiredService<IDbContextFactory<AccountsContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Accounts.Add(new AccountRow { Name = "alice" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return provider;
    }

    internal sealed class AccountRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public bool Disabled { get; set; }
    }

    internal sealed class NoteRow
    {
        public int Id { get; set; }

        public string Text { get; set; } = "";
    }

    internal sealed class AccountsContext(DbContextOptions<AccountsContext> options) : DbContext(options)
    {
        public DbSet<AccountRow> Accounts => Set<AccountRow>();

        public DbSet<NoteRow> Notes => Set<NoteRow>();
    }

    internal sealed class AccountRowCheck(IDbContextFactory<AccountsContext> contexts) : SessionRevalidator<AccountRow>
    {
        private int _loads;

        public int Loads => Volatile.Read(ref _loads);

        protected override async Task<AccountRow?> Load(string userId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loads);
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            return await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Name == userId, cancellationToken);
        }

        protected override ClaimsPrincipal? Principal(AccountRow user, ClaimsPrincipal principal) =>
            user.Disabled ? null : principal;
    }
}
