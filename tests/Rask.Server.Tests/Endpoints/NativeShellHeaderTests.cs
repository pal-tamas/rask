using Rask.Chrome;
using Rask.Chrome.Components;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     A native shell announces itself on the request, and the portable bars then emit no markup — the same
///     <see cref="Screen" /> that renders a header in a browser describes one to the platform instead.
/// </summary>
/// <remarks>
///     The header is read on the initial GET rather than on the WebSocket hello, and these tests are what
///     pin that. By hello-time the document has already been rendered and sent, so bar markup would ship,
///     paint, and then vanish — a flash the "zero bar markup in the document" requirement does not allow.
/// </remarks>
public class NativeShellHeaderTests
{
    [Fact]
    public async Task Without_the_header_the_bars_render_as_html()
    {
        using var host = RaskTestHost.Create<BarApp>();

        var body = await host.Http.GetStringAsync("/");

        // The ordinary web rendering, unchanged — this is the control for the test below.
        Assert.Contains("rask-header-bar", body, StringComparison.Ordinal);
        Assert.Contains("Inbox", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_the_header_the_document_carries_no_bar_markup()
    {
        using var host = RaskTestHost.Create<BarApp>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Rask-Shell", "native");

        using var response = await host.Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("rask-header-bar", body, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-bar-button", body, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A header this server does not understand must not change how it renders. Anything but the one
    ///     known value reads as an ordinary browser.
    /// </summary>
    [Theory]
    [InlineData("web")]
    [InlineData("")]
    [InlineData("Native-ish")]
    public async Task An_unrecognised_header_value_renders_as_the_web(string value)
    {
        using var host = RaskTestHost.Create<BarApp>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Rask-Shell", value);

        using var response = await host.Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("rask-header-bar", body, StringComparison.Ordinal);
    }

    /// <summary>The value is matched case-insensitively — a head is not required to guess our casing.</summary>
    [Fact]
    public async Task The_header_value_is_case_insensitive()
    {
        using var host = RaskTestHost.Create<BarApp>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Rask-Shell", "NATIVE");

        using var response = await host.Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("rask-header-bar", body, StringComparison.Ordinal);
    }
}

/// <summary>
///     An ordinary server app with a portable bar. It names no native type at all — which is the point:
///     the same component serves a browser and a native shell.
/// </summary>
internal sealed partial class BarApp : Component
{
    protected override Component? Render() =>
    [
        AppBar.Title("Inbox"),
        Div["body"],
    ];
}
