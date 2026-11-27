using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.TypedId.Tests;

/// <summary>
///     The owner-approved account: <c>User : Authenticatable&lt;UserId&gt;, IBelongsTo&lt;Tenant&gt;</c>, both ids typed
///     over a Guid — its key, its sessions, its claims and its tenant.
/// </summary>
public sealed class TypedAccountTests
{
    private const string Password = "Password1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_keyed_by_a_typed_id_signs_in_as_the_guid_it_holds()
    {
        await using var harness = await OwnedAsync("admin@rask.test");

        var outcome = await harness.Accounts.ValidateAsync("admin@rask.test", Password, client: null, Cancel);

        Assert.True(outcome.Result.Succeeded, $"{outcome.Result.Error} {outcome.Result.Message}");
        var stored = await harness.UserAsync("admin@rask.test");
        Assert.NotEqual(Guid.Empty, stored.Id.Value);
        Assert.Equal(stored.Id.Value.ToString(), outcome.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(outcome.Principal!.FindFirstValue(Rask.Data.Tenant.ClaimType));
    }

    [Fact]
    public async Task A_session_of_a_typed_account_resumes_and_ends()
    {
        await using var harness = await OwnedAsync("admin@rask.test");
        var held = await SignedInAsync(harness, "admin@rask.test");
        var stored = await harness.UserAsync("admin@rask.test");

        var resumed = await harness.Sessions.ResumeAsync(SessionOf(held), Cancel);
        await harness.Sessions.EndAsync(SessionOf(held), Cancel);
        var ended = await harness.Sessions.ResumeAsync(SessionOf(held), Cancel);

        Assert.Equal(stored.Id.Value.ToString(), resumed!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("admin@rask.test", resumed!.Identity!.Name);
        Assert.Null(ended);
    }

    [Fact]
    public async Task An_account_an_administrator_creates_joins_the_typed_tenant_in_flight()
    {
        await using var harness = await OwnedAsync("admin@rask.test");
        var acme = await harness.AddTenantAsync("Acme");

        var created = await AsAdminAsync(harness, acme, () => Accounts.Create("kovacs@rask.test", Password, Cancel));

        Assert.True(created.Succeeded, $"{created.Error} {created.Message}");
        Assert.Equal(acme, (await harness.UserAsync("kovacs@rask.test")).TenantId);
        Assert.Null((await harness.UserAsync("admin@rask.test")).TenantId);
    }

    [Fact]
    public async Task A_member_of_a_typed_tenant_carries_the_guid_it_holds_and_follows_a_move()
    {
        await using var harness = await OwnedAsync("admin@rask.test");
        var acme = await harness.AddTenantAsync("Acme");
        var globex = await harness.AddTenantAsync("Globex");
        await AsAdminAsync(harness, acme, () => Accounts.Create("kovacs@rask.test", Password, Cancel));
        var held = await SignedInAsync(harness, "kovacs@rask.test");

        await harness.ChangeAsync("kovacs@rask.test", user => user.JoinTenant(globex));
        var moved = await harness.Sessions.RevalidateAsync(SessionOf(held), held, used: true, Cancel);

        Assert.Equal(acme.Value.ToString(), held.FindFirstValue(Rask.Data.Tenant.ClaimType));
        Assert.Equal(globex.Value.ToString(), moved!.FindFirstValue(Rask.Data.Tenant.ClaimType));
    }

    [Fact]
    public async Task An_administrator_acts_in_a_typed_tenant_that_exists_and_not_in_one_that_does_not()
    {
        await using var harness = await OwnedAsync("admin@rask.test");
        var acme = await harness.AddTenantAsync("Acme");
        var held = await SignedInAsync(harness, "admin@rask.test");
        using var read = harness.BindReadFaces();

        var chosen = await harness.Accounts.ActInTenantAsync(held, acme.Value, Cancel);
        var unknown = await harness.Accounts.ActInTenantAsync(held, Guid.CreateVersion7(), Cancel);
        var acting = await harness.Sessions.RevalidateAsync(SessionOf(held), held, used: true, Cancel);

        Assert.True(chosen.Succeeded, $"{chosen.Error} {chosen.Message}");
        Assert.Equal(AuthError.NotAllowed, unknown.Error);
        Assert.Equal(acme.Value.ToString(), acting!.FindFirstValue(Rask.Data.Tenant.ClaimType));
    }

    [Fact]
    public async Task The_typed_user_is_current_user_by_its_own_id()
    {
        await using var harness = await OwnedAsync("admin@rask.test");
        var stored = await harness.UserAsync("admin@rask.test");

        UserId? current;
        using (Current.UseUser(stored.Id))
        {
            current = Current.UserId;
        }

        Assert.Equal(stored.Id, current);
    }

    // The first account registered, with the first-run token: the administrator, of no tenant.
    private static async Task<TypedHarness> OwnedAsync(string email)
    {
        var harness = new TypedHarness();
        await harness.StartAsync();
        var registered = await harness.Accounts.RegisterAsync(email, Password, TypedHarness.FirstRunToken, client: null, Cancel);
        Assert.True(registered.Result.Succeeded, $"{registered.Result.Error} {registered.Result.Message}");

        return harness;
    }

    // The administrator at work in a tenant, as a page of theirs is.
    private static async Task<AuthResult> AsAdminAsync(TypedHarness harness, TenantId tenant, Func<Task<AuthResult>> act)
    {
        var admin = (await harness.UserAsync("admin@rask.test")).Id;
        await using var scope = harness.Services.CreateAsyncScope();
        using (Db.UseScope(scope.ServiceProvider))
        using (Current.UseUser(admin))
        using (Rask.Data.Tenant.Use(tenant))
        {
            return await act();
        }
    }

    // Signed in on one device: the principal a page holds for the session the sign-in started.
    private static async Task<ClaimsPrincipal> SignedInAsync(TypedHarness harness, string email)
    {
        var outcome = await harness.Accounts.ValidateAsync(email, Password, client: null, Cancel);
        Assert.True(outcome.Result.Succeeded, $"{outcome.Result.Error} {outcome.Result.Message}");
        var userId = Guid.Parse(outcome.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var sessionId = await harness.Sessions.StartAsync(userId, null, null, persistent: false, Cancel);

        return (await harness.Sessions.ResumeAsync(sessionId, Cancel))!;
    }

    private static Guid SessionOf(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sid")!);
}
