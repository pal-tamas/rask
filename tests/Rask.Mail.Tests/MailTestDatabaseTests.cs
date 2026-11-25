using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Mailing.Tests;

/// <summary>An app's context, mapping the mail table as a RaskApp's does.</summary>
public sealed class PostContext(DbContextOptions<PostContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskMail();
    }
}

/// <summary>A <c>TestDatabase</c> over a model that maps the mail table stands the mail battery in with its fake.</summary>
public sealed class MailTestDatabaseTests : IDisposable
{
    private readonly InMemorySqlite _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_send_with_no_host_is_recorded_by_the_database_s_mail_fake()
    {
        await using var database = await TestDatabase.Start<PostContext>(o => o.UseSqlite(_db.ConnectionString), cancellationToken: Ct);

        await Mail.Send(Mail.Message.To("ann@x.io").Subject("Welcome").Html("<p>hi</p>"), Ct);

        database.Mail.Sent().To("ann@x.io").Once();
    }

    [Fact]
    public async Task A_mail_battery_the_test_registers_itself_replaces_the_fake()
    {
        var own = new OwnMail();
        await using var database = await TestDatabase.Start<PostContext>(
            o => o.UseSqlite(_db.ConnectionString), services: s => s.AddSingleton<IMail>(own), cancellationToken: Ct);

        var missing = Assert.Throws<InvalidOperationException>(() => database.Mail);

        Assert.False(Mail.IsOn);
        Assert.Contains("registered a mail battery of its own", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_without_the_mail_table_has_no_mail_fake_and_says_how_to_map_it()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(_db.ConnectionString), cancellationToken: Ct);

        var missing = Assert.Throws<InvalidOperationException>(() => database.Mail);

        Assert.Contains("modelBuilder.AddRaskMail()", missing.Message, StringComparison.Ordinal);
    }

    private sealed class OwnMail : IMail
    {
        public Task Add(Mail.Message email, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
