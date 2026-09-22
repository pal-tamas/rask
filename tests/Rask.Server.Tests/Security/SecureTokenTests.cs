using Rask.Core;

namespace Rask.Server.Tests.Security;

// M5: redeem tickets and live-session ids are bearer secrets, so they come from a CSPRNG
// (RandomNumberGenerator) rather than Guid.NewGuid().
public class SecureTokenTests
{
    [Fact]
    public void A_token_is_32_lowercase_hex_characters()
    {
        var token = SecureToken.Create();

        Assert.Equal(32, token.Length); // 16 bytes = 128 bits
        Assert.Matches("^[0-9a-f]{32}$", token);
    }

    [Fact]
    public void Every_token_created_is_unique()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(tokens.Add(SecureToken.Create()), "tokens must be unique");
        }
    }
}
