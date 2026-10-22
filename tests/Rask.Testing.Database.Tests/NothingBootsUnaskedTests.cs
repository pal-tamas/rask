using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;
using Rask.Testing.Fixture.UnsetApp;

namespace Rask.Testing.Database.Tests;

// One class, so its tests run one at a time: TestDatabase points the process-wide model surface at its database.
public sealed class NothingBootsUnaskedTests : IDisposable
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-testing-db-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_test_with_a_database_of_its_own_reads_it_without_booting_the_app()
    {
        using var clock = Clock.Fake(at: Monday);
        await using var database = await Start();
        database.Context.Add(Company.Named("Acme"));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var companies = await Company.Count(TestContext.Current.CancellationToken);

        Assert.Equal(1, companies);
        Assert.Null(Ambient.Services);
    }

    [Fact]
    public async Task A_check_inside_a_removal_reads_the_tests_own_database_and_refuses()
    {
        await using var database = await Start();
        var company = Company.Named("Acme");
        database.Context.AddRange(company, Contact.At(company));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => company.Remove(database.Context));

        Assert.Equal(Company.StillHasContacts, refused.Message);
    }

    [Fact]
    public void A_lookup_with_another_answer_does_not_boot_the_app()
    {
        var services = Ambient.Services;

        Assert.Null(services);
    }

    [Fact]
    public async Task A_read_with_no_database_of_its_own_boots_the_app_it_needs()
    {
        var booted = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Company.Count(TestContext.Current.CancellationToken));

        Assert.Contains(UnsetApp.DatabaseVariable, booted.Message, StringComparison.Ordinal);
    }

    private Task<TestDatabase> Start() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
}
