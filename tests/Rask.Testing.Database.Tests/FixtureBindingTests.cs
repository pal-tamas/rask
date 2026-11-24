using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// A database a class fixture started belongs to no test's flow — InitializeAsync ran in the framework's — so each test
// class binds it in its constructor, and two such classes run side by side, each on its own.

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

public sealed class TwoCompaniesFixtureTests(TwoCompanies fixture) : IClassFixture<TwoCompanies>, IDisposable
{
    private readonly IDisposable _bound = fixture.Database.Bind();

    public void Dispose() => _bound.Dispose();

    [Fact]
    public async Task A_test_reads_the_database_its_class_fixture_started() =>
        Assert.Equal(fixture.Companies, await Company.Count(TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_test_reads_the_fixture_s_database_through_the_read_face_too() =>
        Assert.Equal(fixture.Companies, await Company.Where(c => c.Name.StartsWith("Company")).Count(TestContext.Current.CancellationToken));
}

public sealed class FiveCompaniesFixtureTests(FiveCompanies fixture) : IClassFixture<FiveCompanies>, IDisposable
{
    private readonly IDisposable _bound = fixture.Database.Bind();

    public void Dispose() => _bound.Dispose();

    [Fact]
    public async Task A_test_reads_the_database_its_class_fixture_started() =>
        Assert.Equal(fixture.Companies, await Company.Count(TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_test_reads_the_fixture_s_database_through_the_read_face_too() =>
        Assert.Equal(fixture.Companies, await Company.Where(c => c.Name.StartsWith("Company")).Count(TestContext.Current.CancellationToken));
}
