using System.Text;
using Rask.Core;
using Rask.Core.Live;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Wasm.Tests.Session;

// The differ pairs attributes by name, so two attributes written in the other order are no change to the tree. The
// markup is still not the one the browser has, and a reply that answered that with the whole page landed on whatever
// the reader had typed since. The tree is what the browser holds: nothing is sent.
[Collection("WasmSession")]
public sealed class ReorderedAttributesTests() : ResettingTestBase(LiveDiffMode.Auto)
{
    [Fact]
    public async Task A_render_that_only_writes_two_attributes_in_the_other_order_is_answered_with_no_page()
    {
        var (session, _) = NewSession<ReorderingApp>(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var press = Encoding.UTF8.GetBytes($$"""{"id":"{{MarkupAssert.FirstHandlerId(initial)}}","type":"click"}""");
        var changed = await session.DispatchAsync(press);

        var reordered = await session.DispatchAsync(press);

        Assert.StartsWith("{\"kind\":\"diff\"", Encoding.UTF8.GetString(changed), StringComparison.Ordinal);
        Assert.Empty(reordered);
    }
}

/// <summary>A button that says "pressed" once, and writes two attributes of a box in the other order at every press.</summary>
internal sealed partial class ReorderingApp : Component
{
    private int _presses;

    protected override Component? HeadAssets => Title["reordering"];

    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        (string, string?)[] attributes = _presses % 2 == 0 ? [("data-a", "1"), ("data-b", "2")] : [("data-b", "2"), ("data-a", "1")];

        return
        [
            Div.Id("box").Attributes(attributes)[P[_presses > 0 ? "pressed" : "waiting"]],
            Button.OnClick(() => _presses++)["press"],
        ];
    }
}
