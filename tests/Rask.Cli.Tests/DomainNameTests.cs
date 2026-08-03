namespace Rask.Cli.Tests;

/// <summary>
/// <see cref="DomainName"/> is a security boundary, so these read like <see cref="SshTargetTests"/>: a
/// handful of shapes that must be accepted, and a catalogue of the injections that must not be.
/// </summary>
public sealed class DomainNameTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("app.example.com")]
    [InlineData("a.b.c.d.example.co.uk")]
    [InlineData("my-app.example.com")]
    [InlineData("xn--bcher-kva.example.com")] // punycode — already ASCII by the time it reaches us
    [InlineData("localhost")]
    [InlineData("app1.example2.com")]
    [InlineData("*.example.com")]             // Caddy accepts one wildcard label
    public void Accepts_host_names(string value)
    {
        Assert.True(DomainName.TryParse(value, out var domain, out var error), error);
        Assert.Equal(value, domain);
    }

    [Fact]
    public void Trims_surrounding_whitespace()
    {
        Assert.True(DomainName.TryParse("  app.example.com \n", out var domain, out _));
        Assert.Equal("app.example.com", domain);
    }

    /// <summary>
    /// The reason this type exists. Each of these would otherwise be written verbatim into the Caddyfile
    /// that fronts every app on the box — closing the generated site block and opening attacker-chosen
    /// directives — or forge a row in the tab-separated <c>docker ps</c> label listing.
    /// </summary>
    [Theory]
    [InlineData("app.example.com {\n}\n:80 {\n\trespond \"pwned\"", "closes the site block and opens another")]
    [InlineData("app.example.com {\n\tfile_server browse\n}\n#", "injects a directive into the app's own block")]
    [InlineData("example.com\n{\n\tadmin 0.0.0.0:2019\n}", "opens Caddy's admin API to the world")]
    [InlineData("evil\tapp\tother.example.com\tblue", "forges a row in the docker ps label listing")]
    [InlineData("app.example.com\rmalicious.example.com", "smuggles a second name past a line-oriented reader")]
    [InlineData("app.example.com respond \"x\"", "a space starts a second Caddy token")]
    [InlineData("$(id).example.com", "command substitution, if the value is ever pasted into a shell")]
    [InlineData("`id`.example.com", "backtick command substitution")]
    [InlineData("app.example.com;rm -rf /", "shell metacharacters")]
    public void Rejects_injection(string value, string why)
    {
        Assert.False(DomainName.TryParse(value, out _, out var error), why);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading-dash.example.com")]
    [InlineData("trailing-dash-.example.com")]
    [InlineData("double..dot.example.com")]
    [InlineData("under_score.example.com")]  // legal in DNS records, not in a host name
    [InlineData("*")]                        // a wildcard with nothing to qualify it
    [InlineData("app.*.example.com")]        // a wildcard that isn't the first label
    public void Rejects_malformed_names(string value) =>
        Assert.False(DomainName.TryParse(value, out _, out _));

    [Fact]
    public void Rejects_an_over_long_name()
    {
        var tooLong = string.Join('.', Enumerable.Repeat("abcdefghij", 26)); // 26 * 11 - 1 = 285 chars
        Assert.False(DomainName.TryParse(tooLong, out _, out var error));
        Assert.Contains("253", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_over_long_label() =>
        Assert.False(DomainName.TryParse(new string('a', 64) + ".example.com", out _, out _));

    /// <summary>
    /// The error text is printed to a terminal and into CI logs, so the rejected value must not be able to
    /// smuggle control characters through the very message that reports it.
    /// </summary>
    [Fact]
    public void Error_message_does_not_echo_control_characters()
    {
        Assert.False(DomainName.TryParse("evil\n\tFAKE LOG LINE.example.com", out _, out var error));
        Assert.DoesNotContain('\n', error!);
        Assert.DoesNotContain('\t', error!);
    }
}
