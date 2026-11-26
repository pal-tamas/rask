using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>
///     Test.FormShows tells what only the store refuses — a name already taken, a reference to no row of the tenant's —
///     as the form does when the field is left: through an operation's dry run, and a generated model's store rules.
/// </summary>
public sealed class FormShowsStoreTests
{
    private static readonly Guid Acme = new("ae5f3f6b-5e0a-4b2c-9f4e-0000000000a1");
    private static readonly Guid Globex = new("ae5f3f6b-5e0a-4b2c-9f4e-0000000000b2");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_name_another_row_holds_is_shown_under_its_field_by_a_factorys_form()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        using var acme = Tenant.Use(Acme);
        await Wharf.Build("North", Ct);

        var shows = Test.FormShows<WharfBuildInput>(m => m.Name, "North", Wharf.NameTaken);

        await shows;
        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_name_another_row_holds_is_shown_under_its_field_by_an_operations_form()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        using var acme = Tenant.Use(Acme);
        await Wharf.Build("North", Ct);
        var south = await Wharf.Build("South", Ct);

        var shows = Test.FormShows(await WharfRenameInput.Of(south, Ct), m => m.Name, "North", Wharf.NameTaken);

        await shows;
        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_reference_to_another_tenants_row_is_shown_under_its_field_by_the_models_form()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var theirs = WharfOperator.Named("globex");
        using (Tenant.Use(Globex))
        {
            await theirs.Save(cancellationToken: Ct);
        }

        using var acme = Tenant.Use(Acme);

        var shows = Test.FormShows<WharfModel>(m => m.WharfOperatorId, theirs.Id, given: m => m.Name = "North", Wharf.NoSuchOperator);

        await shows;
        Assert.True(shows.IsCompletedSuccessfully);
    }
}
