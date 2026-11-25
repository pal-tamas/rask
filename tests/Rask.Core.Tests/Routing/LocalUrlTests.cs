using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// Shared open-redirect guard (Rask.Core.Routing.LocalUrl) used by the server post-sign-in
// returnUrl, the client route-guard challenge, and the WASM login/logout flows. Only same-origin
// absolute paths pass through; everything that could leave the origin collapses to "/".
public class LocalUrlTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/", "/")]
    [InlineData("/a/b?x=1#frag", "/a/b?x=1#frag")]
    public void A_local_absolute_path_passes_through(string input, string expected)
        => Assert.Equal(expected, LocalUrl.Sanitize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.com")] // protocol-relative
    [InlineData("/\\evil.com")] // backslash → browser normalises to "//"
    [InlineData("\\evil.com")] // leading backslash
    [InlineData("https://evil.com")] // absolute URL
    [InlineData("javascript:alert(1)")] // not rooted
    [InlineData("evil.com")] // relative, not rooted
    [InlineData("/foo\r\nSet-Cookie: x")] // control chars
    [InlineData("/foo\tbar")]
    public void A_non_local_or_malformed_url_collapses_to_the_root(string? input)
        => Assert.Equal("/", LocalUrl.Sanitize(input));

    [Theory]
    [InlineData("/login?returnUrl=%2Forders%3Ftab%3D2")]
    [InlineData("/a%20b?x=%25")]
    [InlineData("//evil.com")]
    public void Sanitizing_an_already_escaped_url_twice_is_sanitizing_it_once(string input)
    {
        var once = LocalUrl.Sanitize(input);

        var twice = LocalUrl.Sanitize(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void An_already_escaped_local_url_comes_back_unchanged()
    {
        const string escaped = "/login?returnUrl=%2Forders%3Ftab%3D2";

        var sanitized = LocalUrl.Sanitize(escaped);

        Assert.Equal(escaped, sanitized);
    }

    [Fact]
    public void A_local_url_at_the_cap_is_kept_and_one_past_it_falls_back_to_the_root()
    {
        var atCap = "/" + new string('a', LocalUrl.MaxLength - 1);

        var (kept, dropped) = (LocalUrl.Sanitize(atCap), LocalUrl.Sanitize(atCap + "a"));

        Assert.Equal(atCap, kept);
        Assert.Equal("/", dropped);
    }
}
