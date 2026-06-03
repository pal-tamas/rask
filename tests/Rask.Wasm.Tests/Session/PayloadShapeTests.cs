using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedAssets;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
// Asserts against the `html` payload field — force the legacy full-HTML wire shape
// (framework default is LiveDiffMode.Auto).
public class PayloadShapeTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{

    [Fact]
    public async Task InitialRender_AlwaysIncludesDataRaskRootEqualsWasm()
    {
        var (session, _) = NewSession();

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("data-rask-root=\"wasm\"", html);
    }

    [Fact]
    public async Task InitialRender_NoCssRegistered_DoesNotIncludeCssText()
    {
        var (session, _) = NewSession();

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
    }

    [Fact]
    public async Task InitialRender_FollowedByHandlerDispatch_DoesNotResendCssText_WhenHashUnchanged()
    {
        var (session, _) = NewSession();
        var initial = await session.InitialRenderAsync();
        var handlerId = Markup.FirstHandlerId(initial);

        var payload =
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));

        using var doc = JsonDocument.Parse(payload.AsMemory());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
    }
}
