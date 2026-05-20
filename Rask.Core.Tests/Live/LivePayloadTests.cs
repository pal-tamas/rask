using System.Text;
using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class LivePayloadTests
{
    [Fact]
    public void InjectRootAttr_AddsDataRaskRoot_OnlyOnFirstBodyTag()
    {
        const string html = "<html><body><body class=\"x\"></body></body></html>";

        var injected = LivePayload.InjectRootAttr(html, "abc");

        Assert.Contains("<body data-rask-root=\"abc\"", injected);
        Assert.Equal(1, CountOccurrences(injected, "data-rask-root"));
    }

    [Fact]
    public void InjectRootAttr_HtmlEncodesSessionId()
    {
        const string html = "<html><body></body></html>";

        var injected = LivePayload.InjectRootAttr(html, "<script>");

        Assert.Contains("data-rask-root=\"&lt;script&gt;\"", injected);
        Assert.DoesNotContain("<script>", injected[..injected.IndexOf("</body>", StringComparison.Ordinal)]);
    }

    [Fact]
    public void ExtractBody_ReturnsBodyElement_WhenPresent()
    {
        const string html = "<html><head></head><body class=\"a\"><p>hi</p></body></html>";

        Assert.Equal("<body class=\"a\"><p>hi</p></body>", LivePayload.ExtractBody(html));
    }

    [Fact]
    public void ExtractBody_ReturnsInputUnchanged_WhenNoBody()
    {
        const string html = "<div>just a fragment</div>";

        Assert.Equal(html, LivePayload.ExtractBody(html));
    }

    [Fact]
    public void BuildPayload_NoHistoryNoCss_EmitsHtmlAndCssHashOnly()
    {
        var payload = LivePayload.BuildPayload("<body></body>", null, false);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal("<body></body>", root.GetProperty("html").GetString());
        Assert.True(root.TryGetProperty("cssHash", out _));
        Assert.False(root.TryGetProperty("history", out _));
        Assert.False(root.TryGetProperty("cssText", out _));
    }

    [Fact]
    public void BuildPayload_CssTextOnly_AddsCssTextField()
    {
        var payload = LivePayload.BuildPayload("<body></body>", null, false, ".x{}");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal(".x{}", root.GetProperty("cssText").GetString());
        Assert.False(root.TryGetProperty("history", out _));
    }

    [Fact]
    public void BuildPayload_HistoryPush_EmitsActionPush()
    {
        var payload = LivePayload.BuildPayload("<body></body>", "/foo", false);

        using var doc = JsonDocument.Parse(payload);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/foo", history.GetProperty("url").GetString());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
    }

    [Fact]
    public void BuildPayload_HistoryReplace_EmitsActionReplace()
    {
        var payload = LivePayload.BuildPayload("<body></body>", "/foo", true);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public void BuildPayload_HistoryAndCssText_AddsBothFields()
    {
        var payload = LivePayload.BuildPayload("<body></body>", "/foo", false, ".x{}");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal(".x{}", root.GetProperty("cssText").GetString());
        Assert.Equal("push", root.GetProperty("history").GetProperty("action").GetString());
        Assert.Equal("/foo", root.GetProperty("history").GetProperty("url").GetString());
    }

    [Fact]
    public void ExtractBodyUtf8_ReturnsBodySlice_WhenPresent()
    {
        const string html = "<html><head></head><body class=\"a\"><p>hi</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);

        var body = LivePayload.ExtractBodyUtf8(bytes);

        Assert.Equal("<body class=\"a\"><p>hi</p></body>", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void ExtractBodyUtf8_CaseInsensitive_AndMixedCase()
    {
        const string html = "<HTML><BODY class=\"a\"><p>hi</p></Body></HTML>";
        var bytes = Encoding.UTF8.GetBytes(html);

        var body = LivePayload.ExtractBodyUtf8(bytes);

        Assert.Equal("<BODY class=\"a\"><p>hi</p></Body>", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void ExtractBodyUtf8_ReturnsInputUnchanged_WhenNoBody()
    {
        const string html = "<div>just a fragment</div>";
        var bytes = Encoding.UTF8.GetBytes(html);

        var body = LivePayload.ExtractBodyUtf8(bytes);

        Assert.Equal(html, Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void BuildPayloadUtf8WithBody_MatchesChainedStringPath()
    {
        const string html = "<html><head></head><body class=\"a\"><p>hi</p></body></html>";

        var legacy = LivePayload.BuildPayloadUtf8(
            LivePayload.ExtractBody(LivePayload.InjectRootAttr(html, "session-123")),
            null,
            false);

        var direct = LivePayload.BuildPayloadUtf8WithBody(
            html,
            "session-123",
            null,
            false);

        Assert.Equal(Encoding.UTF8.GetString(legacy), Encoding.UTF8.GetString(direct));
    }

    [Fact]
    public void BuildPayloadUtf8WithBody_HtmlEncodesSessionId()
    {
        const string html = "<html><body></body></html>";

        var payload = LivePayload.BuildPayloadUtf8WithBody(html, "<script>", null, false);

        using var doc = JsonDocument.Parse(payload);
        var body = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("data-rask-root=\"&lt;script&gt;\"", body);
    }

    [Fact]
    public void BuildPayloadUtf8WithBody_NoBody_FallsBackToFullHtml()
    {
        const string html = "<div>fragment only</div>";

        var payload = LivePayload.BuildPayloadUtf8WithBody(html, "sid", null, false);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(html, doc.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public void BuildPayloadUtf8WithBody_PreservesHistoryAndCssTextFields()
    {
        const string html = "<html><body></body></html>";

        var payload = LivePayload.BuildPayloadUtf8WithBody(html, "sid", "/foo", true, ".x{}");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal(".x{}", root.GetProperty("cssText").GetString());
        var history = root.GetProperty("history");
        Assert.Equal("replace", history.GetProperty("action").GetString());
        Assert.Equal("/foo", history.GetProperty("url").GetString());
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }
}
