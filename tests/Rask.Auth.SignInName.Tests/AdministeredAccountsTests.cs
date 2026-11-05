using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.SignInName.Tests;

/// <summary>
///     An administrator manages the accounts of the tenant they work in (#1302): <c>Accounts.Create</c>,
///     <c>Rename</c>, <c>SetPassword</c> and <c>Remove</c>, on an app shaped as the one that asked — keyed by text,
///     signing in by name, its users in tenants and its administrators in none.
/// </summary>
[Collection(NameDbCollection.Name)]
public sealed class AdministeredAccountsTests
{
    private const string Password = "Password1";
    private const string NewPassword = "NewPassword2";

    private static readonly Guid Acme = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid Globex = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_an_administrator_creates_signs_nobody_in_and_signs_in_with_its_password()
    {
        await using var harness = await AdministeredAsync();
        await using var scope = harness.Services.CreateAsyncScope();

        AuthResult created;
        using (Db.UseScope(scope.ServiceProvider))
        using (Current.UseUser(await IdOfAsync(harness, "admin")))
        using (Tenant.Use(Acme))
        {
            created = await Accounts.Create(" kovacs ", Password, Cancel);
        }

        Assert.True(created.Succeeded, $"{created.Error} {created.Message}");
        var stored = await harness.UserAsync("kovacs");
        Assert.Equal(Acme, stored.TenantId);
        Assert.Equal(new[] { RaskRoles.User }, stored.Roles);
        Assert.Equal(0, await SessionCountAsync(harness));
        Assert.True((await harness.Accounts.ValidateAsync("kovacs", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task A_name_any_account_holds_is_refused_in_every_tenant()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));

        var sameTenant = await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs ", Password, Cancel));
        var otherTenant = await AsAdminAsync(harness, Globex, accounts => accounts.Create("kovacs", Password, Cancel));
        var theAdministrators = await AsAdminAsync(harness, Acme, accounts => accounts.Create("admin", Password, Cancel));

        Assert.Equal(AuthError.DuplicateAccount, sameTenant.Error);
        Assert.Equal(AuthError.DuplicateAccount, otherTenant.Error);
        Assert.Equal(AuthError.DuplicateAccount, theAdministrators.Error);
        Assert.Equal(2, await AccountCountAsync(harness));
    }

    [Fact]
    public async Task A_new_account_s_password_follows_the_registration_policy()
    {
        await using var harness = await AdministeredAsync();

        var created = await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", "short", Cancel));

        Assert.Equal(AuthError.WeakPassword, created.Error);
        Assert.NotNull(created.Message);
        Assert.Equal(1, await AccountCountAsync(harness));
    }

    [Fact]
    public async Task A_rename_to_another_account_s_name_is_refused_and_one_to_a_free_name_signs_in()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("nagy", Password, Cancel));
        var nagy = (await harness.UserAsync("nagy")).Id;

        var taken = await AsAdminAsync(harness, Acme, accounts => accounts.Rename(nagy, " kovacs", Cancel));
        var free = await AsAdminAsync(harness, Acme, accounts => accounts.Rename(nagy, " szabo ", Cancel));

        Assert.Equal(AuthError.DuplicateAccount, taken.Error);
        Assert.True(free.Succeeded, $"{free.Error} {free.Message}");
        Assert.Equal(nagy, (await harness.UserAsync("szabo")).Id);
        Assert.True((await harness.Accounts.ValidateAsync("szabo", Password, client: null, Cancel)).Result.Succeeded);
        Assert.False((await harness.Accounts.ValidateAsync("nagy", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task A_rename_to_its_own_name_is_not_a_taken_name()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = (await harness.UserAsync("kovacs")).Id;

        var renamed = await AsAdminAsync(harness, Acme, accounts => accounts.Rename(kovacs, "kovacs", Cancel));

        Assert.True(renamed.Succeeded, $"{renamed.Error} {renamed.Message}");
    }

    [Fact]
    public async Task A_password_an_administrator_sets_ends_that_user_s_sessions_and_not_the_administrator_s()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = (await harness.UserAsync("kovacs")).Id;
        var theirs = await harness.Sessions.StartAsync(Guid.Parse(kovacs), null, null, persistent: false, Cancel);
        var admins = await harness.Sessions.StartAsync(await IdOfAsync(harness, "admin"), null, null, persistent: false, Cancel);

        var set = await AsAdminAsync(harness, Acme, accounts => accounts.SetPassword(kovacs, NewPassword, Cancel));

        Assert.True(set.Succeeded, $"{set.Error} {set.Message}");
        Assert.Null(await harness.Sessions.ResumeAsync(theirs, Cancel));
        Assert.NotNull(await harness.Sessions.ResumeAsync(admins, Cancel));
        Assert.True((await harness.Accounts.ValidateAsync("kovacs", NewPassword, client: null, Cancel)).Result.Succeeded);
        Assert.False((await harness.Accounts.ValidateAsync("kovacs", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task A_blank_password_leaves_the_password_and_the_sessions_as_they_are()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = (await harness.UserAsync("kovacs")).Id;
        var theirs = await harness.Sessions.StartAsync(Guid.Parse(kovacs), null, null, persistent: false, Cancel);

        var blank = await AsAdminAsync(harness, Acme, accounts => accounts.SetPassword(kovacs, "  ", Cancel));
        var none = await AsAdminAsync(harness, Acme, accounts => accounts.SetPassword(kovacs, null, Cancel));

        Assert.True(blank.Succeeded);
        Assert.True(none.Succeeded);
        Assert.NotNull(await harness.Sessions.ResumeAsync(theirs, Cancel));
        Assert.True((await harness.Accounts.ValidateAsync("kovacs", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task A_removed_account_takes_its_sessions_with_it()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = (await harness.UserAsync("kovacs")).Id;
        var theirs = await harness.Sessions.StartAsync(Guid.Parse(kovacs), null, null, persistent: false, Cancel);

        await AsAdminAsync(harness, Acme, accounts => accounts.Remove(kovacs, Cancel));

        Assert.Equal(1, await AccountCountAsync(harness));
        Assert.Null(await harness.Sessions.ResumeAsync(theirs, Cancel));
        Assert.Equal(0, await SessionCountAsync(harness));
        Assert.False((await harness.Accounts.ValidateAsync("kovacs", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task A_removal_inside_a_transaction_that_fails_is_rolled_back_with_it()
    {
        await using var harness = await AdministeredAsync(services => services.AddRaskData<NameDb>());
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = (await harness.UserAsync("kovacs")).Id;
        var theirs = await harness.Sessions.StartAsync(Guid.Parse(kovacs), null, null, persistent: false, Cancel);
        await using var scope = harness.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccounts>();

        using (Db.UseScope(scope.ServiceProvider))
        using (Current.UseUser(await IdOfAsync(harness, "admin")))
        using (Tenant.Use(Acme))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
            {
                await accounts.Remove(kovacs, Cancel);
                throw new InvalidOperationException("The rest of the work failed.");
            }, cancellationToken: Cancel));
        }

        Assert.Equal(kovacs, (await harness.UserAsync("kovacs")).Id);
        Assert.NotNull(await harness.Sessions.ResumeAsync(theirs, Cancel));
    }

    [Fact]
    public async Task Another_tenant_s_account_is_not_found_and_nothing_is_changed()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = (await harness.UserAsync("kovacs")).Id;

        var rename = await Record.ExceptionAsync(() => AsAdminAsync(harness, Globex, accounts => accounts.Rename(kovacs, "nagy", Cancel)));
        var password = await Record.ExceptionAsync(() => AsAdminAsync(harness, Globex, accounts => accounts.SetPassword(kovacs, NewPassword, Cancel)));
        var remove = await Record.ExceptionAsync(() => AsAdminAsync(harness, Globex, accounts => accounts.Remove(kovacs, Cancel)));

        Assert.IsType<KeyNotFoundException>(rename);
        Assert.IsType<KeyNotFoundException>(password);
        Assert.IsType<KeyNotFoundException>(remove);
        Assert.Equal(kovacs, (await harness.UserAsync("kovacs")).Id);
        Assert.True((await harness.Accounts.ValidateAsync("kovacs", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task An_administrator_s_account_and_an_id_nobody_has_are_not_found()
    {
        await using var harness = await AdministeredAsync();
        var admin = (await harness.UserAsync("admin")).Id;

        var adminsPassword = await Record.ExceptionAsync(() => AsAdminAsync(harness, tenant: null, accounts => accounts.SetPassword(admin, NewPassword, Cancel)));
        var adminRemoved = await Record.ExceptionAsync(() => AsAdminAsync(harness, tenant: null, accounts => accounts.Remove(admin, Cancel)));
        var unknown = await Record.ExceptionAsync(() => AsAdminAsync(harness, Acme, accounts => accounts.Remove(Guid.NewGuid(), Cancel)));
        var notAKey = await Record.ExceptionAsync(() => AsAdminAsync(harness, Acme, accounts => accounts.Remove("not-a-key", Cancel)));

        Assert.IsType<KeyNotFoundException>(adminsPassword);
        Assert.IsType<KeyNotFoundException>(adminRemoved);
        Assert.IsType<KeyNotFoundException>(unknown);
        Assert.IsType<KeyNotFoundException>(notAKey);
        Assert.True((await harness.Accounts.ValidateAsync("admin", Password, client: null, Cancel)).Result.Succeeded);
    }

    [Fact]
    public async Task A_signed_in_user_who_is_not_an_administrator_and_nobody_at_all_are_refused()
    {
        await using var harness = await AdministeredAsync();
        await AsAdminAsync(harness, Acme, accounts => accounts.Create("kovacs", Password, Cancel));
        var kovacs = await IdOfAsync(harness, "kovacs");
        await using var scope = harness.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccounts>();

        ForbiddenException member;
        ForbiddenException nobody;
        using (Tenant.Use(Acme))
        {
            using (Current.UseUser(kovacs))
            {
                member = await Assert.ThrowsAsync<ForbiddenException>(() => accounts.Create("nagy", Password, Cancel));
            }

            nobody = await Assert.ThrowsAsync<ForbiddenException>(() => accounts.Remove(kovacs, Cancel));
        }

        Assert.True(member.IsAuthenticated);
        Assert.False(nobody.IsAuthenticated);
        Assert.Equal(2, await AccountCountAsync(harness));
    }

    // The first account registered, by name, with the first-run token: the administrator, who belongs to no tenant.
    private static async Task<NameHarness> AdministeredAsync(Action<IServiceCollection>? extraServices = null)
    {
        var harness = new NameHarness(extraServices: extraServices);
        await harness.StartAsync();
        var registered = await harness.Accounts.RegisterAsync("admin", Password, NameHarness.FirstRunToken, client: null, Cancel);
        Assert.True(registered.Result.Succeeded, $"{registered.Result.Error} {registered.Result.Message}");

        return harness;
    }

    // What an admin page's handler does: the signed-in administrator, working in the tenant they chose.
    private static async Task<T> AsAdminAsync<T>(NameHarness harness, Guid? tenant, Func<IAccounts, Task<T>> work)
    {
        var admin = await IdOfAsync(harness, "admin");
        await using var scope = harness.Services.CreateAsyncScope();
        using var asAdmin = Current.UseUser(admin);
        using var inTenant = tenant is { } chosen ? Tenant.Use(chosen) : null;

        return await work(scope.ServiceProvider.GetRequiredService<IAccounts>());
    }

    private static Task<bool> AsAdminAsync(NameHarness harness, Guid? tenant, Func<IAccounts, Task> work) =>
        AsAdminAsync(harness, tenant, async accounts =>
        {
            await work(accounts);
            return true;
        });

    private static async Task<Guid> IdOfAsync(NameHarness harness, string name) =>
        Guid.Parse((await harness.UserAsync(name)).Id);

    private static async Task<int> AccountCountAsync(NameHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<User>().CountAsync(Cancel);
    }

    // Sessions that have not ended: an ended one keeps its row, soft-deleted, until SessionSweep takes it.
    private static async Task<int> SessionCountAsync(NameHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<Session>().IgnoreQueryFilters().CountAsync(s => s.DeletedAt == null, Cancel);
    }
}
