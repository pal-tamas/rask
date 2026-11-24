using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// A database a class fixture started belongs to no test's flow — InitializeAsync ran in the framework's — so Rask.Testing's
// test hook binds it before each test of the class, and two such classes run side by side, each on its own.

/// <summary>A class fixture's database, seeded with <see cref="Companies" /> companies.</summary>
public abstract class SeededDatabase(int companies) : IAsyncLifetime
{
    public int Companies { get; } = companies;

    public TestDatabase<PumpsContext> Database { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Database = await TestDatabase.Start<PumpsContext>();
        for (var i = 0; i < Companies; i++)
        {
            Database.Context.Add(Company.Named($"Company {i}"));
        }

        await Database.Context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed class TwoCompanies() : SeededDatabase(companies: 2);

public sealed class FiveCompanies() : SeededDatabase(companies: 5);

public sealed class TwoCompaniesFixtureTests(TwoCompanies fixture) : IClassFixture<TwoCompanies>
{
    [Fact]
    public async Task A_test_reads_the_database_its_class_fixture_started() =>
        Assert.Equal(fixture.Companies, await Company.Count(TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_test_reads_the_fixture_s_database_through_the_read_face_too() =>
        Assert.Equal(fixture.Companies, await Company.Where(c => c.Name.StartsWith("Company")).Count(TestContext.Current.CancellationToken));
}

public sealed class FiveCompaniesFixtureTests(FiveCompanies fixture) : IClassFixture<FiveCompanies>
{
    [Fact]
    public async Task A_test_reads_the_database_its_class_fixture_started() =>
        Assert.Equal(fixture.Companies, await Company.Count(TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_test_reads_the_fixture_s_database_through_the_read_face_too() =>
        Assert.Equal(fixture.Companies, await Company.Where(c => c.Name.StartsWith("Company")).Count(TestContext.Current.CancellationToken));
}

public sealed class BindTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Binding_a_database_started_in_another_flow_points_the_test_at_it_over_a_later_one()
    {
        await using var first = await Task.Run(() => Seeded(companies: 4), Ct);
        await using var later = await Task.Run(() => Seeded(companies: 1), Ct);

        using var bound = first.Bind();
        var companies = await Company.Count(Ct);

        Assert.Equal(4, companies);
    }

    // An async method of its own, so the flow it binds the database to ends with it.
    private static async Task<TestDatabase<PumpsContext>> Seeded(int companies)
    {
        var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        for (var i = 0; i < companies; i++)
        {
            database.Context.Add(Company.Named($"Company {i}"));
        }

        await database.Context.SaveChangesAsync(Ct);
        return database;
    }
}
