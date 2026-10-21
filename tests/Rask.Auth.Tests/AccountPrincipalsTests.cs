using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Auth.Tests;

/// <summary>What a job enqueued by a user is checked against at run time: the principal their account issues now.</summary>
public sealed class AccountPrincipalsTests
{
    [Fact]
    public async Task A_users_principal_is_rebuilt_from_their_account_as_it_is_now()
    {
        await using var harness = new AuthHarness();
        var id = await AddAsync(harness, "ada@example.com", "editor");
        var accounts = harness.Services.GetRequiredService<IAccountPrincipals>();

        var principal = await accounts.Of(id, TestContext.Current.CancellationToken);

        Assert.NotNull(principal);
        Assert.Equal(id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True(principal.IsInRole("editor"));
    }

    [Fact]
    public async Task A_user_with_no_account_any_more_has_no_principal()
    {
        await using var harness = new AuthHarness();
        var accounts = harness.Services.GetRequiredService<IAccountPrincipals>();

        var principal = await accounts.Of(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(principal);
    }

    private static async Task<Guid> AddAsync(AuthHarness harness, string email, string role)
    {
        await using var db = harness.NewContext();
        var user = TestUser.Blank();
        user.GrantRole(role);
        db.Add(user);
        var id = Guid.CreateVersion7();
        var entry = db.Entry(user);
        entry.Property(nameof(Authenticatable.Id)).CurrentValue = id;
        entry.Property(nameof(Authenticatable.Email)).CurrentValue = Authenticatable.NormalizeEmail(email);
        entry.Property("PasswordHash").CurrentValue = "x";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }
}
