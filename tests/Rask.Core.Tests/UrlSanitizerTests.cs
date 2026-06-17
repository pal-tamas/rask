#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories
#pragma warning disable RASK023 // these tests exercise src/URL sanitization, not alt text

namespace Rask.Core.Tests;

public class UrlSanitizerTests
{
    // --- dangerous schemes are neutralized on navigation/resource attributes ---

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)")] // leading whitespace stripped before scheme
    [InlineData("java\tscript:alert(1)")] // embedded tab removed before scheme
    [InlineData("java\nscript:alert(1)")] // embedded newline removed before scheme
    [InlineData("java\0script:alert(1)")] // embedded NUL removed before scheme
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Href_DangerousScheme_NeutralizedToAboutBlank(string url)
    {
        Assert.Equal("<a href=\"about:blank\"></a>", A(url).ToHtml());
    }

    [Theory]
    [InlineData("/local/path", "/local/path")]
    [InlineData("https://example.com/x?a=b", "https://example.com/x?a=b")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("mailto:a@b.com", "mailto:a@b.com")]
    [InlineData("tel:+123", "tel:&#x2B;123")] // '+' HTML-encoded by the attribute encoder
    [InlineData("#frag", "#frag")]
    [InlineData("?q=1", "?q=1")]
    [InlineData("relative/path:withcolon", "relative/path:withcolon")] // not a scheme
    public void Href_SafeUrl_PassesThrough(string url, string expected)
    {
        Assert.Equal($"<a href=\"{expected}\"></a>", A(url).ToHtml());
    }

    [Fact]
    public void IframeSrc_JavascriptScheme_Neutralized() =>
        Assert.Equal("<iframe src=\"about:blank\"></iframe>", Iframe("javascript:alert(1)").ToHtml());

    [Fact]
    public void IframeSrc_DataHtml_Neutralized() =>
        Assert.Equal("<iframe src=\"about:blank\"></iframe>", Iframe("data:text/html,<x>").ToHtml());

    // --- media attributes allow inline data: for image/video/audio only ---

    [Fact]
    public void ImgSrc_DataImage_PassesThrough() =>
        Assert.Equal(
            "<img src=\"data:image/png;base64,iVBOR\" />",
            Img("data:image/png;base64,iVBOR").ToHtml());

    [Fact]
    public void ImgSrc_DataSvg_PassesThrough() =>
        Assert.Equal(
            "<img src=\"data:image/svg&#x2B;xml,abc\" />", // '+' HTML-encoded
            Img("data:image/svg+xml,abc").ToHtml());

    [Fact]
    public void ImgSrc_DataHtml_Neutralized() =>
        Assert.Equal("<img src=\"about:blank\" />", Img("data:text/html,<x>").ToHtml());

    [Fact]
    public void ImgSrc_Javascript_Neutralized() =>
        Assert.Equal("<img src=\"about:blank\" />", Img("javascript:alert(1)").ToHtml());

    // --- RaskUrl.Trusted opt-out round-trips verbatim (still HTML-encoded) ---

    [Fact]
    public void Href_Trusted_BypassesSanitization() =>
        Assert.Equal(
            "<a href=\"javascript:void(0)\"></a>",
            A(RaskUrl.Trusted("javascript:void(0)")).ToHtml());

    [Fact]
    public void Href_Trusted_StillHtmlEncoded() =>
        Assert.Equal(
            "<a href=\"/x?a=1&amp;b=2\"></a>",
            A(RaskUrl.Trusted("/x?a=1&b=2")).ToHtml());

    // --- value still HTML-encoded after sanitization (no attribute breakout) ---

    [Fact]
    public void Href_QuoteInSafeUrl_Encoded() =>
        Assert.Equal(
            "<a href=\"/a&quot;b\"></a>",
            A("/a\"b").ToHtml());

    [Fact]
    public void NullHref_OmitsAttribute() => Assert.Equal("<a></a>", A().ToHtml());
}
