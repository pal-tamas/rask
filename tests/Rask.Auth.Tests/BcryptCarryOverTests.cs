using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     The bcrypt hashes an app moving onto Rask's sign-in brings with it — <c>$2a$</c>/<c>$2b$</c> at cost 10, as
///     BCrypt.Net-Next and BCrypt-Core write them — sign in with the password their owner types today, accents and all,
///     and are renewed to PBKDF2 on that sign-in.
/// </summary>
/// <remarks>
///     Both libraries hash the password's UTF-8 bytes and read only the first 72 of them, and each verifies the other's
///     hashes (checked by hand for BCrypt-Core 2.0.0 against BCrypt.Net-Next 4.2.0 on these passwords). So a bcrypt hash
///     of a longer password also matches anything that starts with those 72 bytes; once renewed, the whole password counts.
/// </remarks>
public sealed class BcryptCarryOverTests
{
    private const string Owner = "owner@example.com";
    private const string Claim = "Claimant-password-1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string, char> Passwords => new()
    {
        { "árvíztűrő tükörfúrógép", 'a' },
        { "árvíztűrő tükörfúrógép", 'b' },
        { "Őszi szél: ű ő á Ű Ő Á", 'a' },
        { "Őszi szél: ű ő á Ű Ő Á", 'b' },
        { new string('ő', 40) + "x", 'a' },
        { new string('a', 80), 'b' },
    };

    [Theory]
    [MemberData(nameof(Passwords))]
    public async Task A_carried_over_hash_signs_in_with_the_same_password_and_is_renewed_to_pbkdf2(string password, char revision)
    {
        await using var harness = await ClaimedAsync();
        await BearerSignInTests.StoreHashAsync(harness, Owner, Bcrypt(password, revision));

        var first = await SignInAsync(harness, password);
        var renewed = (await harness.UserAsync(Owner))!.PasswordHash;
        var again = await SignInAsync(harness, password);
        var wrong = await SignInAsync(harness, password + "!");

        Assert.True(first.Succeeded, $"{first.Error} {first.Message}");
        Assert.StartsWith("$rask$pbkdf2-sha256$", renewed, StringComparison.Ordinal);
        Assert.True(again.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, wrong.Error);
    }

    [Fact]
    public async Task A_password_past_72_bytes_must_be_typed_whole_once_its_hash_is_renewed()
    {
        var password = new string('ő', 40) + "x";
        var sameFirst72Bytes = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(password), 0, 72) + "anything";
        await using var harness = await ClaimedAsync();
        await BearerSignInTests.StoreHashAsync(harness, Owner, Bcrypt(password, 'a'));

        var beforeRenewal = await SignInAsync(harness, sameFirst72Bytes);
        await BearerSignInTests.StoreHashAsync(harness, Owner, Bcrypt(password, 'a'));
        var renewal = await SignInAsync(harness, password);
        var afterRenewal = await SignInAsync(harness, sameFirst72Bytes);

        Assert.True(beforeRenewal.Succeeded);
        Assert.True(renewal.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, afterRenewal.Error);
    }

    private static string Bcrypt(string password, char revision) =>
        BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(10, revision));

    private static async Task<AuthResult> SignInAsync(AuthHarness harness, string password)
    {
        using var scope = harness.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IAccountFlows>()
            .ValidateAsync(Owner, password, client: null, Cancel);
        return outcome.Result;
    }

    private static async Task<AuthHarness> ClaimedAsync()
    {
        var harness = new AuthHarness();
        await harness.StartAsync();
        var claimed = await harness.Services.GetRequiredService<IAccountFlows>()
            .RegisterAsync(Owner, Claim, AuthHarness.FirstRunTokenValue, client: null, Cancel);
        Assert.True(claimed.Result.Succeeded, $"{claimed.Result.Error} {claimed.Result.Message}");

        return harness;
    }
}
