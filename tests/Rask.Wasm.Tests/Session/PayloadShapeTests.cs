using System.Text;
using System.Text.Json;
using Rask.Core.Live;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
// Asserts against the `html` payload field — force the legacy full-HTML wire shape
// (framework default is LiveDiffMode.Auto).
public class PayloadShapeTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task The_initial_render_always_includes_data_rask_root_equal_to_wasm()
    {
        var (session, _) = NewSession(diffMode: DiffMode);

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("data-rask-root=\"wasm\"", html);
    }

    [Fact]
    public async Task With_no_CSS_registered_the_initial_render_carries_no_cssText()
    {
        var (session, _) = NewSession(diffMode: DiffMode);

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
    }

    [Fact]
    public async Task A_dispatch_after_the_initial_render_does_not_resend_cssText_when_the_hash_is_unchanged()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        var payload =
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handlerId}}","type":"click"}"""));

        using var doc = JsonDocument.Parse(payload.AsMemory());
        Assert.False(doc.RootElement.TryGetProperty("cssText", out _));
    }
}
