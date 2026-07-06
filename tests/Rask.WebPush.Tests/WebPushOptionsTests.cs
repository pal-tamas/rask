namespace Rask.WebPush.Tests;

// Coverage for WebPushOptions.Validate — the config guard the sender runs once when it resolves.
public class WebPushOptionsTests
{
    private static WebPushOptions Valid() => new()
    {
        VapidKeys = VapidKeys.Generate(),
        Subject = "mailto:admin@example.com",
    };

    [Fact]
    public void Validate_passes_for_a_well_formed_mailto_config() =>
        Valid().Validate(); // must not throw

    [Fact]
    public void Validate_accepts_an_https_subject()
    {
        var options = Valid();
        options.Subject = "https://example.com/contact";
        options.Validate(); // must not throw
    }

    [Fact]
    public void Validate_rejects_missing_keys()
    {
        var options = Valid();
        options.VapidKeys = null;
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_rejects_an_empty_subject()
    {
        var options = Valid();
        options.Subject = "";
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("admin@example.com")] // no scheme
    [InlineData("tel:+15550100")]     // wrong scheme
    [InlineData("http://example.com")] // http, not https
    public void Validate_rejects_a_subject_without_mailto_or_https(string subject)
    {
        var options = Valid();
        options.Subject = subject;
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Validate_rejects_a_negative_default_ttl()
    {
        var options = Valid();
        options.DefaultTtl = TimeSpan.FromSeconds(-1);
        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
