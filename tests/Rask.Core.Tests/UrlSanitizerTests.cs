#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories
#pragma warning disable RASK023 // these tests exercise src/URL sanitization, not alt text

namespace Rask.Core.Tests;

public partial class UrlSanitizerTests : global::Rask.Core.RaskMarkup
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
    public void An_href_with_a_dangerous_scheme_is_neutralized_to_about_blank(string url)
    {
        Assert.Equal("<a href=\"about:blank\"></a>", A.Href(url).ToHtml());
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
    public void An_href_with_a_safe_url_passes_through(string url, string expected)
    {
        Assert.Equal($"<a href=\"{expected}\"></a>", A.Href(url).ToHtml());
    }

    [Fact]
    public void A_javascript_scheme_iframe_src_is_neutralized() =>
        Assert.Equal("<iframe src=\"about:blank\"></iframe>", Iframe.Src("javascript:alert(1)").ToHtml());

    [Fact]
    public void A_data_html_iframe_src_is_neutralized() =>
        Assert.Equal("<iframe src=\"about:blank\"></iframe>", Iframe.Src("data:text/html,<x>").ToHtml());

    // --- media attributes allow inline data: for image/video/audio only ---

    [Fact]
    public void A_data_image_img_src_passes_through() =>
        Assert.Equal(
            "<img src=\"data:image/png;base64,iVBOR\" />",
            Img.Src("data:image/png;base64,iVBOR").ToHtml());

    [Fact]
    public void A_data_svg_img_src_passes_through() =>
        Assert.Equal(
            "<img src=\"data:image/svg&#x2B;xml,abc\" />", // '+' HTML-encoded
            Img.Src("data:image/svg+xml,abc").ToHtml());

    [Fact]
    public void A_data_html_img_src_is_neutralized() =>
        Assert.Equal("<img src=\"about:blank\" />", Img.Src("data:text/html,<x>").ToHtml());

    [Fact]
    public void A_javascript_img_src_is_neutralized() =>
        Assert.Equal("<img src=\"about:blank\" />", Img.Src("javascript:alert(1)").ToHtml());

    // --- RaskUrl.Trusted opt-out round-trips verbatim (still HTML-encoded) ---

    [Fact]
    public void A_trusted_href_bypasses_sanitization() =>
        Assert.Equal(
            "<a href=\"javascript:void(0)\"></a>",
            A.Href(RaskUrl.Trusted("javascript:void(0)")).ToHtml());

    [Fact]
    public void A_trusted_href_is_still_html_encoded() =>
        Assert.Equal(
            "<a href=\"/x?a=1&amp;b=2\"></a>",
            A.Href(RaskUrl.Trusted("/x?a=1&b=2")).ToHtml());

    // --- value still HTML-encoded after sanitization (no attribute breakout) ---

    [Fact]
    public void A_quote_in_a_safe_url_href_is_encoded() =>
        Assert.Equal(
            "<a href=\"/a&quot;b\"></a>",
            A.Href("/a\"b").ToHtml());

    [Fact]
    public void A_null_href_omits_the_attribute() => Assert.Equal("<a></a>", A.ToHtml());
}
