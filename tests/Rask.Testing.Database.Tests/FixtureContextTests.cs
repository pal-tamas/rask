using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

// database.Context is the fixture's one long-lived context: what it tracks is kept as the database has it, and a refused
// save of its own does not ride along with the next — as a page's context, gone with its request, would not.
public sealed class FixtureContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_row_another_context_saved_reads_as_saved_through_the_fixture_context()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var company = Company.Named("Acme");
        database.Context.Add(company);
        await database.Context.SaveChangesAsync(Ct);

        await RenameElsewhere(database, company.Id, "Acme Kft.");
        var read = await database.Context.Set<Company>().SingleAsync(c => c.Id == company.Id, Ct);

        Assert.Same(company, read);
        Assert.Equal("Acme Kft.", read.Name);
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(read).State);
    }

    [Fact]
    public async Task A_row_another_context_saved_is_changed_and_saved_again_through_the_fixture_context()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var company = Company.Named("Acme");
        database.Context.Add(company);
        await database.Context.SaveChangesAsync(Ct);
        await RenameElsewhere(database, company.Id, "Acme Kft.");

        company.Rename("Acme Zrt.");
        await database.Context.SaveChangesAsync(Ct);

        Assert.Equal("Acme Zrt.", await NameInTheDatabase(database, company.Id));
    }

    [Fact]
    public async Task A_change_the_test_has_not_saved_is_kept_when_another_context_saves_the_row()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var company = Company.Named("Acme");
        database.Context.Add(company);
        await database.Context.SaveChangesAsync(Ct);
        company.Rename("Mine");

        await RenameElsewhere(database, company.Id, "Theirs");

        Assert.Equal("Mine", company.Name);
        Assert.Equal(EntityState.Modified, database.Context.Entry(company).State);
    }

    [Fact]
    public async Task A_refused_save_of_the_fixture_context_does_not_ride_along_with_the_next()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var taken = Company.Named("First");
        await using (var other = database.Factory.CreateDbContext())
        {
            other.Add(taken);
            await other.SaveChangesAsync(Ct);
        }

        var twin = Company.Named("Twin");
        database.Context.Entry(twin).Property(c => c.Id).CurrentValue = taken.Id;
        database.Context.Add(twin);
        var refused = await Record.ExceptionAsync(() => database.Context.SaveChangesAsync(Ct));
        database.Context.Add(Company.Named("Second"));
        await database.Context.SaveChangesAsync(Ct);

        Assert.NotNull(refused);
        Assert.Equal(["First", "Second"], await NamesInTheDatabase(database));
    }

    private static async Task RenameElsewhere(TestDatabase<PumpsContext> database, Guid id, string name)
    {
        await using var other = database.Factory.CreateDbContext();
        var company = await other.Set<Company>().SingleAsync(c => c.Id == id, Ct);
        company.Rename(name);
        await other.SaveChangesAsync(Ct);
    }

    private static async Task<string> NameInTheDatabase(TestDatabase<PumpsContext> database, Guid id)
    {
        await using var fresh = database.Factory.CreateDbContext();
        return await fresh.Set<Company>().Where(c => c.Id == id).Select(c => c.Name).SingleAsync(Ct);
    }

    private static async Task<List<string>> NamesInTheDatabase(TestDatabase<PumpsContext> database)
    {
        await using var fresh = database.Factory.CreateDbContext();
        return await fresh.Set<Company>().OrderBy(c => c.Name).Select(c => c.Name).ToListAsync(Ct);
    }
}
