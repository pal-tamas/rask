using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>
///     A save of the fixture's own context refused before it reaches the database — a reference to another tenant's row —
///     leaves nothing behind for the next save to carry.
/// </summary>
public sealed class FixtureContextTenantRefusalTests
{
    private static readonly Guid Acme = new("ae5f3f6b-5e0a-4b2c-9f4e-0000000000c3");
    private static readonly Guid Globex = new("ae5f3f6b-5e0a-4b2c-9f4e-0000000000d4");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_save_refused_for_another_tenants_reference_does_not_ride_along_with_the_next()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var theirs = WharfOperator.Named("globex");
        using (Tenant.Use(Globex))
        {
            await theirs.Save(cancellationToken: Ct);
        }

        using var acme = Tenant.Use(Acme);
        var north = await Wharf.Build("North", Ct);
        var tracked = await database.Context.Set<Wharf>().SingleAsync(w => w.Id == north, Ct);

        tracked.RunBy(theirs.Id);
        var refused = await Record.ExceptionAsync(() => database.Context.SaveChangesAsync(Ct));
        database.Context.Add(WharfOperator.Named("acme"));
        await database.Context.SaveChangesAsync(Ct);

        Assert.IsType<RaskValidationException>(refused);
        await using var fresh = database.Factory.CreateDbContext();
        Assert.Null((await fresh.Set<Wharf>().SingleAsync(Ct)).WharfOperatorId);
        Assert.Single(await fresh.Set<WharfOperator>().ToListAsync(Ct));
    }
}
