using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     An administrator manages accounts that sign in with their address (#1302). The text-keyed, name-signing,
///     tenanted shape is <c>Rask.Auth.SignInName.Tests.AdministeredAccountsTests</c>; this is what an address adds:
///     the link that confirms it, and the passkeys it may have.
/// </summary>
[Collection(AuthDbCollection.Name)]
public sealed class AdministeredAccountsTests
{
    private const string Password = "Password1";
    private const string Owner = "owner@example.com";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_created_by_address_signs_in_and_is_sent_the_link_that_confirms_it()
    {
        await using var harness = await AdministeredAsync();

        var created = await AsOwnerAsync(harness, accounts => accounts.Create(" Kovacs@Example.com ", Password, Cancel));

        Assert.True(created.Succeeded, $"{created.Error} {created.Message}");
        var stored = await harness.UserAsync("kovacs@example.com");
        Assert.NotNull(stored);
        Assert.Null(stored.TenantId);
        Assert.False(stored.IsEmailConfirmed);
        Assert.NotNull(harness.Mail!.LastTo("kovacs@example.com")?.Link);
        Assert.True((await SignInAsync(harness, "kovacs@example.com", Password)).Succeeded);
    }

    [Fact]
    public async Task The_app_s_own_columns_are_set_on_a_new_account_before_it_is_saved()
    {
        await using var harness = await AdministeredAsync();

        var created = await AsOwnerAsync(
            harness, accounts => accounts.Create("kovacs@example.com", Password, (TestUser u) => u.Rename("Kovács Anna"), Cancel));

        Assert.True(created.Succeeded, $"{created.Error} {created.Message}");
        Assert.Equal("Kovács Anna", (await harness.UserAsync("kovacs@example.com"))!.DisplayName);
    }

    [Fact]
    public async Task Something_that_is_not_an_address_is_refused_as_a_name()
    {
        await using var harness = await AdministeredAsync();

        var created = await AsOwnerAsync(harness, accounts => accounts.Create("kovacs", Password, Cancel));

        Assert.Equal(AuthError.InvalidEmail, created.Error);
        Assert.Equal(1, await harness.UserCountAsync());
    }

    [Fact]
    public async Task A_new_address_is_unconfirmed_again_and_sent_a_link_while_the_same_one_is_left_alone()
    {
        await using var harness = await AdministeredAsync();
        await AsOwnerAsync(harness, accounts => accounts.Create("kovacs@example.com", Password, Cancel));
        var id = (await harness.UserAsync("kovacs@example.com"))!.Id;
        await ConfirmAsync(harness, id);
        var sentBefore = harness.Mail!.Sent.Count;

        var same = await AsOwnerAsync(harness, accounts => accounts.Rename(id, "KOVACS@example.com", Cancel));
        var confirmedAfterSame = (await harness.UserAsync("kovacs@example.com"))!.IsEmailConfirmed;
        var moved = await AsOwnerAsync(harness, accounts => accounts.Rename(id, "anna@example.com", Cancel));

        Assert.True(same.Succeeded);
        Assert.True(confirmedAfterSame);
        Assert.True(moved.Succeeded, $"{moved.Error} {moved.Message}");
        Assert.False((await harness.UserAsync("anna@example.com"))!.IsEmailConfirmed);
        Assert.Equal(sentBefore + 1, harness.Mail.Sent.Count);
        Assert.NotNull(harness.Mail.LastTo("anna@example.com"));
    }

    [Fact]
    public async Task A_removed_account_takes_its_passkeys_with_it()
    {
        await using var harness = await AdministeredAsync();
        await AsOwnerAsync(harness, accounts => accounts.Create("kovacs@example.com", Password, Cancel));
        var id = (await harness.UserAsync("kovacs@example.com"))!.Id;
        await using (var db = harness.NewContext())
        {
            db.Add(Passkey.Register(id, "Phone", [1, 2, 3], [4, 5, 6], -7, 0, backedUp: false, null, DateTime.UtcNow));
            await db.SaveChangesAsync(Cancel);
        }

        await AsOwnerAsync(harness, accounts => accounts.Remove(id, Cancel));

        Assert.Null(await harness.UserAsync("kovacs@example.com"));
        await using var after = harness.NewContext();
        Assert.Equal(0, await after.Set<Passkey>().IgnoreQueryFilters().CountAsync(Cancel));
    }

    // The first account registered, with the first-run token, is the administrator; a mail battery catches the links.
    private static async Task<AuthHarness> AdministeredAsync()
    {
        var harness = new AuthHarness(mail: true);
        await harness.StartAsync();
        using var scope = harness.NewScope();
        var registered = await scope.ServiceProvider.GetRequiredService<AccountService<TestUser, Guid>>()
            .RegisterAsync(Owner, Password, AuthHarness.FirstRunTokenValue, client: null, Cancel);
        Assert.True(registered.Result.Succeeded, $"{registered.Result.Error} {registered.Result.Message}");

        return harness;
    }

    // What an admin page's handler does, as the administrator in an app without tenants.
    private static async Task<T> AsOwnerAsync<T>(AuthHarness harness, Func<IAccounts, Task<T>> work)
    {
        var owner = (await harness.UserAsync(Owner))!.Id;
        using var scope = harness.NewScope();
        using var asOwner = Current.UseUser(owner);

        return await work(scope.ServiceProvider.GetRequiredService<IAccounts>());
    }

    private static Task<bool> AsOwnerAsync(AuthHarness harness, Func<IAccounts, Task> work) =>
        AsOwnerAsync(harness, async accounts =>
        {
            await work(accounts);
            return true;
        });

    private static async Task ConfirmAsync(AuthHarness harness, Guid id)
    {
        await using var db = harness.NewContext();
        var user = await db.Set<TestUser>().SingleAsync(u => u.Id == id, Cancel);
        user.ConfirmEmail(DateTime.UtcNow);
        await db.SaveChangesAsync(Cancel);
    }

    private static async Task<AuthResult> SignInAsync(AuthHarness harness, string email, string password)
    {
        using var scope = harness.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<AccountService<TestUser, Guid>>()
            .ValidateAsync(email, password, client: null, Cancel);
        return outcome.Result;
    }
}
