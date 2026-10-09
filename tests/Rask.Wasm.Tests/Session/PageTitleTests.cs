using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// #1239 in the browser host: the frame a WASM session boots with, and every navigation frame after it,
// already has the page's title in the layout's crumb and in the <title> its App writes above the router.
[Collection("WasmSession")]
public class PageTitleTests : ResettingTestBase
{
    [Fact]
    public async Task The_first_frame_carries_the_title_in_the_crumb_and_in_the_document_title()
    {
        var (session, services) = NewSession<PageTitleStubApp>(diffMode: LiveDiffMode.DisabledFull);
        services.GetRequiredService<RouteState>().Path = "/wt/edit/7";

        var frame = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(frame.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString();
        Assert.Contains(">Edit Record 7 | Stub</title>", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"crumb\">Edit Record 7</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_navigation_frame_carries_the_new_crumb_and_the_new_document_title()
    {
        var (session, services) = NewSession<PageTitleStubApp>(diffMode: LiveDiffMode.DisabledFull);
        services.GetRequiredService<RouteState>().Path = "/wt/list";
        await session.InitialRenderAsync();

        var frame = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/wt/edit/7","query":""}"""));

        using var doc = JsonDocument.Parse(frame.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString();
        Assert.Contains(">Edit Record 7 | Stub</title>", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"crumb\">Edit Record 7</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Records", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_navigation_on_the_diff_path_carries_the_new_title_as_a_head_fragment()
    {
        var (session, services) = NewSession<PageTitleStubApp>(diffMode: LiveDiffMode.Forced);
        services.GetRequiredService<RouteState>().Path = "/wt/edit/7";
        await session.InitialRenderAsync();

        var frame = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/wt/edit/8","query":""}"""));

        using var doc = JsonDocument.Parse(frame.AsMemory());
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.Contains("Edit Record 8 | Stub", doc.RootElement.GetProperty("head").GetString(), StringComparison.Ordinal);
        Assert.Contains("Edit Record 8", doc.RootElement.GetProperty("ops").GetRawText(), StringComparison.Ordinal);
    }
}
