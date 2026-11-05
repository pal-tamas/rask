using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.SignInName.Tests;

/// <summary>
///     An account keyed by text that signs in by user name (#1279): <c>User : Authenticatable&lt;string&gt;</c> with
///     <c>[SignInName] Username</c>. In <see cref="NameDbCollection" />, so its tests run one after another with the
///     other classes': they share one context type, and EF Core's model cache is per context type.
/// </summary>
[Collection(NameDbCollection.Name)]
public sealed class SignInByNameTests
{
    private const string Password = "Password1";

    private static readonly Guid Acme = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_keyed_by_text_signs_in_by_its_name()
    {
        await using var harness = await OwnedAsync("admin");

        var outcome = await harness.Accounts.ValidateAsync(" admin ", Password, client: null, Cancel);

        Assert.True(outcome.Result.Succeeded, $"{outcome.Result.Error} {outcome.Result.Message}");
        var stored = await harness.UserAsync("admin");
        Assert.Equal(Guid.Parse(stored.Id).ToString(), stored.Id);
        var principal = outcome.Principal!;
        Assert.Equal(stored.Id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("admin", principal.Identity!.Name);
        Assert.Null(principal.FindFirstValue(ClaimTypes.Email));
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_name_are_refused_alike()
    {
        await using var harness = await OwnedAsync("admin");

        var wrongPassword = await harness.Accounts.ValidateAsync("admin", "WrongPassword1", client: null, Cancel);
        var unknownName = await harness.Accounts.ValidateAsync("nobody", Password, client: null, Cancel);

        Assert.Equal(wrongPassword.Result, unknownName.Result);
        Assert.Equal(AuthError.InvalidCredentials, unknownName.Result.Error);
        Assert.Null(wrongPassword.Principal);
        Assert.Null(unknownName.Principal);
    }

    [Fact]
    public async Task A_bcrypt_hash_from_an_existing_table_verifies_and_is_rehashed_to_the_default()
    {
        await using var harness = await OwnedAsync("admin");
        var moved = Guid.NewGuid().ToString();
        await InsertExistingAsync(harness, moved, "kovacs", BCrypt.Net.BCrypt.HashPassword("Jelszo123", 10));

        var outcome = await harness.Accounts.ValidateAsync("kovacs", "Jelszo123", client: null, Cancel);

        Assert.True(outcome.Result.Succeeded, $"{outcome.Result.Error} {outcome.Result.Message}");
        Assert.Equal(moved, outcome.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.False((await harness.UserAsync("kovacs")).PasswordHash.StartsWith("$2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_already_taken_is_not_registered_again()
    {
        await using var harness = await OwnedAsync("admin");

        var again = await harness.Accounts.RegisterAsync("admin", Password, firstRunToken: null, client: null, Cancel);

        Assert.Equal(AuthError.DuplicateAccount, again.Result.Error);
    }

    [Fact]
    public async Task The_flows_that_send_mail_say_an_account_signed_in_by_name_has_no_address()
    {
        await using var harness = await OwnedAsync("admin");
        var id = (await harness.UserAsync("admin")).Id;

        var reset = await harness.Accounts.SendPasswordResetAsync("admin", client: null, Cancel);
        var confirm = await harness.Accounts.ConfirmEmailAsync(id, "token", Cancel);
        var passkey = await harness.Accounts.BeginAddPasskeyAsync(Guid.Parse(id), origin: null, Cancel);

        Assert.Equal(AuthError.NotAllowed, reset.Error);
        Assert.Contains("no address", reset.Message, StringComparison.Ordinal);
        Assert.Equal(reset, confirm);
        Assert.Null(passkey);
    }

    [Fact]
    public async Task Requiring_a_confirmed_address_stops_the_host_starting()
    {
        await using var harness = new NameHarness(o => o.RequireConfirmedEmail = true);

        var refused = Assert.ThrowsAny<Exception>(() => harness.Services.GetRequiredService<AuthOptions>());

        Assert.Contains("RequireConfirmedEmail", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_of_an_account_keyed_by_text_resumes_with_its_name_and_ends_with_a_sign_out()
    {
        await using var harness = await OwnedAsync("admin");
        var held = await SignedInAsync(harness, "admin");

        var resumed = await harness.Sessions.ResumeAsync(SessionOf(held), Cancel);
        await harness.Accounts.SignOutEverywhereAsync(held, Cancel);

        Assert.Equal("admin", resumed!.Identity!.Name);
        Assert.Equal(held.FindFirstValue(ClaimTypes.NameIdentifier), resumed.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(await harness.Sessions.ResumeAsync(SessionOf(held), Cancel));
    }

    [Fact]
    public async Task An_administrator_keyed_by_text_acts_in_the_tenant_they_chose()
    {
        await using var harness = await OwnedAsync("admin");
        var held = await SignedInAsync(harness, "admin");

        var chosen = await harness.Accounts.ActInTenantAsync(held, Acme, Cancel);
        var revalidated = await harness.Sessions.RevalidateAsync(SessionOf(held), held, used: true, Cancel);

        Assert.True(chosen.Succeeded, $"{chosen.Error} {chosen.Message}");
        Assert.Equal(Acme.ToString(), revalidated!.FindFirstValue(Tenant.ClaimType));
    }

    [Fact]
    public async Task A_user_keyed_by_text_carries_their_tenant_and_may_not_choose_another()
    {
        await using var harness = await OwnedAsync("admin");
        await harness.Accounts.RegisterAsync("member", Password, firstRunToken: null, client: null, Cancel);
        await using (var db = harness.NewContext())
        {
            var member = await db.Set<User>().SingleAsync(u => u.Username == new Username("member"), Cancel);
            member.JoinTenant(Acme);
            await db.SaveChangesAsync(Cancel);
        }

        var held = await SignedInAsync(harness, "member");
        var chosen = await harness.Accounts.ActInTenantAsync(held, Guid.NewGuid(), Cancel);

        Assert.Equal(AuthError.NotAllowed, chosen.Error);
        Assert.Equal(Acme.ToString(), held.FindFirstValue(Tenant.ClaimType));
    }

    [Fact]
    public void The_generated_read_face_carries_the_text_key_and_the_name_as_its_value()
    {
        var face = typeof(UserRead);

        var id = face.GetProperty(nameof(UserRead.Id))!.PropertyType;
        var name = face.GetProperty(nameof(UserRead.Username))!.PropertyType;

        Assert.Equal(typeof(string), id);
        Assert.Equal(typeof(string), name);
    }

    // The first account registered, by name, with the first-run token: the administrator.
    private static async Task<NameHarness> OwnedAsync(string name)
    {
        var harness = new NameHarness();
        await harness.StartAsync();
        var registered = await harness.Accounts.RegisterAsync(name, Password, NameHarness.FirstRunToken, client: null, Cancel);
        Assert.True(registered.Result.Succeeded, $"{registered.Result.Error} {registered.Result.Message}");

        return harness;
    }

    // Signed in on one device: the principal a page holds for the session the sign-in started.
    private static async Task<ClaimsPrincipal> SignedInAsync(NameHarness harness, string name)
    {
        var outcome = await harness.Accounts.ValidateAsync(name, Password, client: null, Cancel);
        var userId = Guid.Parse(outcome.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var sessionId = await harness.Sessions.StartAsync(userId, null, null, persistent: false, Cancel);

        return (await harness.Sessions.ResumeAsync(sessionId, Cancel))!;
    }

    private static Guid SessionOf(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sid")!);

    // A row as the table it moved from holds it: its own key, its name, its old hash, written straight to the table.
    private static async Task InsertExistingAsync(NameHarness harness, string id, string name, string hash)
    {
        await using var db = harness.NewContext();
        var entry = db.Entry(User.Blank());
        entry.Property(nameof(User.Id)).CurrentValue = id;
        entry.Property(nameof(User.Username)).CurrentValue = new Username(name);
        entry.Property(nameof(User.PasswordHash)).CurrentValue = hash;
        entry.State = EntityState.Added;
        await db.SaveChangesAsync(Cancel);
    }
}
