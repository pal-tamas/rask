using Rask.Server;

namespace Rask.Server.Tests.Security;

/// <summary>
///     Open-redirect guard for the post-sign-in returnUrl. Only same-origin absolute
///     paths may pass through; everything that could navigate off the origin — absolute
///     URLs, protocol-relative URLs, and the backslash variants browsers normalise into
///     them — must collapse to "/". Mirrors the contract of ASP.NET's Url.IsLocalUrl.
/// </summary>
public class SanitizeReturnUrlTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/", "/")]
    [InlineData("/a/b?x=1#frag", "/a/b?x=1#frag")]
    public void Local_AbsolutePath_PassesThrough(string input, string expected)
        => Assert.Equal(expected, RaskEndpointExtensions.SanitizeReturnUrl(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.com")]                 // protocol-relative
    [InlineData("/\\evil.com")]                // backslash → browser normalises to "//"
    [InlineData("\\evil.com")]                 // leading backslash
    [InlineData("\\\\evil.com")]
    [InlineData("https://evil.com")]           // absolute URL
    [InlineData("http://evil.com")]
    [InlineData("javascript:alert(1)")]        // not rooted
    [InlineData("evil.com")]                    // relative, not rooted
    [InlineData("/foo\r\nSet-Cookie: x")]      // control chars
    [InlineData("/foo\tbar")]
    public void NonLocal_OrMalformed_CollapsesToRoot(string? input)
        => Assert.Equal("/", RaskEndpointExtensions.SanitizeReturnUrl(input));
}
