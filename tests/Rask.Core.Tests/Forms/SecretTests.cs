using System.Globalization;
using System.Text.Json;
using Rask.Values;

namespace Rask.Core.Tests.Forms;

/// <summary>A secret never shows what it holds — not to a log, an interpolation, the debugger or a serializer.</summary>
public sealed class SecretTests
{
    [Fact]
    public void A_secret_prints_as_its_mask_wherever_it_is_formatted()
    {
        var key = Secret.Of("s3cr3t");

        var interpolated = string.Format(CultureInfo.InvariantCulture, "key={0}", key);

        Assert.Equal("••••", key.ToString());
        Assert.Equal("key=••••", interpolated);
        Assert.Equal("s3cr3t", key.Reveal());
    }

    [Fact]
    public void A_serializer_writes_a_secret_as_an_empty_object()
    {
        var holder = new { Name = "Derby", Key = Secret.Of("s3cr3t") };

        var json = JsonSerializer.Serialize(holder);

        Assert.Equal("""{"Name":"Derby","Key":{}}""", json);
    }

    [Fact]
    public void Two_secrets_holding_the_same_value_are_equal()
    {
        var one = Secret.Of("a");

        var same = Secret.Of("a");

        Assert.Equal(one, same);
        Assert.NotEqual(one, Secret.Of("b"));
    }

    [Fact]
    public void A_change_keeps_sets_or_clears_the_stored_secret()
    {
        var stored = Secret.Of("old");

        var kept = Secret.Applied(Change<string>.Keep, stored);
        var set = Secret.Applied(Change<string>.Set("new"), stored);
        var cleared = Secret.Applied(Change<string>.Clear, stored);

        Assert.Same(stored, kept);
        Assert.Equal("new", set!.Reveal());
        Assert.Null(cleared);
    }
}
