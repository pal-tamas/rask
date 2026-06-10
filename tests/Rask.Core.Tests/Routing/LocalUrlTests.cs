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
    public void Local_AbsolutePath_PassesThrough(string input, string expected)
        => Assert.Equal(expected, LocalUrl.Sanitize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.com")]            // protocol-relative
    [InlineData("/\\evil.com")]           // backslash → browser normalises to "//"
    [InlineData("\\evil.com")]            // leading backslash
    [InlineData("https://evil.com")]      // absolute URL
    [InlineData("javascript:alert(1)")]   // not rooted
    [InlineData("evil.com")]              // relative, not rooted
    [InlineData("/foo\r\nSet-Cookie: x")] // control chars
    [InlineData("/foo\tbar")]
    public void NonLocal_OrMalformed_CollapsesToRoot(string? input)
        => Assert.Equal("/", LocalUrl.Sanitize(input));
}
