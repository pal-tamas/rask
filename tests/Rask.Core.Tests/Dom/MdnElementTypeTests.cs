namespace Rask.Core.Tests.Dom;

// An element type is the DOM interface MDN names; several tags share one the way the DOM shares it, and the
// chain entries stay named after the tags.
public partial class MdnElementTypeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_entry_builds_the_DOM_interface_its_tag_uses()
    {
        var built = new Component[] { A, Em, Section, H3, Td, Th, Audio, Input.Of<string>() };

        var types = built.Select(static c => c.GetType()).ToArray();

        Assert.Equal(
            [
                typeof(HTMLAnchorElement), typeof(HTMLElement), typeof(HTMLElement), typeof(HTMLHeadingElement),
                typeof(HTMLTableCellElement), typeof(HTMLTableCellElement), typeof(HTMLAudioElement), typeof(HTMLInputElement<string>),
            ],
            types);
    }

    [Fact]
    public void Tags_that_share_a_type_each_render_their_own_tag()
    {
        var shared = Div[Em["a"], Strong["b"], H2["c"], H5["d"], Td["e"], Th["f"]];

        var html = shared.ToHtml();

        Assert.Equal("<div><em>a</em><strong>b</strong><h2>c</h2><h5>d</h5><td>e</td><th>f</th></div>", html);
    }

    [Fact]
    public async Task A_slot_reused_by_another_tag_of_the_same_type_renders_the_new_tag()
    {
        var emphatic = true;
        var page = Page.Render(() => Div[emphatic ? Em["hi"] : Strong["hi"], Button.OnClick(() => emphatic = !emphatic)["flip"]]);

        var html = await page.On("button").ClickAsync();

        Assert.Contains("<strong>hi</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<em>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_control_is_the_MDN_type_underneath()
    {
        var input = Input.Of<int>();

        var mdn = (HTMLInputElement)input;

        Assert.True(typeof(HTMLInputElement).IsAbstract);
        Assert.Same(input, mdn);
    }
}
