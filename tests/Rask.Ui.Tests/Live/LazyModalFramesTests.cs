using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A lazy modal sends what it holds only while it is open: closed it is the dialog alone, and the browser's word
///     that it opened is answered with one frame that carries the content.
/// </summary>
public partial class LazyModalFramesTests : global::Rask.Core.RaskMarkup
{
    private const string Secret = "Fields nobody asked for";

    private int _count;

    private Component Lazy() =>
    [
        Ui.Button.Id("count").OnClick(() => _count++)[$"Pressed {_count}"],
        Ui.ModalTrigger.Name("lazy")[Ui.Button["Open"]],
        Ui.Modal.Name("lazy").Lazy()[Ui.Heading[Secret], Ui.Input.Of<string>().Label("Name")]
    ];

    private static Task Toggle(Page page, bool open) =>
        page.On("dialog#lazy > input[data-rask-modal-shown]").Change(open ? "true" : "false");

    [Fact]
    public void A_closed_lazy_modal_renders_its_dialog_and_close_button_and_nothing_it_holds()
    {
        var page = Page.Render(Lazy);

        var html = page.Render();

        Assert.True(page.Exists("dialog#lazy[data-rask-modal]"));
        Assert.True(page.Exists("dialog#lazy [data-ui-focus-placeholder]"));
        Assert.True(page.Exists("dialog#lazy [aria-label=\"Close modal\"]"));
        Assert.Equal("false", page.Find("dialog#lazy > input[data-rask-modal-shown]").Attribute("value"));
        Assert.DoesNotContain(Secret, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_renders_again_while_the_lazy_modal_is_closed_sends_none_of_what_it_holds()
    {
        var page = Page.Render(Lazy);

        await page.On("#count").Click();
        await page.On("#count").Click();

        Assert.Equal(2, _count);
        Assert.NotEmpty(DiffGuardAttribute.Frames());
        Assert.DoesNotContain(DiffGuardAttribute.Frames(), frame => frame.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Opening_a_lazy_modal_is_answered_with_one_frame_that_carries_what_it_holds()
    {
        var page = Page.Render(Lazy);

        await Toggle(page, open: true);

        var frame = Assert.Single(DiffGuardAttribute.Frames());
        Assert.Contains(Secret, frame, StringComparison.Ordinal);
        Assert.True(page.Exists("dialog#lazy [data-ui-input]"));
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task Closing_a_lazy_modal_takes_what_it_holds_away_again()
    {
        var page = Page.Render(Lazy);
        await Toggle(page, open: true);

        await Toggle(page, open: false);

        Assert.DoesNotContain(Secret, page.Render(), StringComparison.Ordinal);
        Assert.Equal(2, DiffGuardAttribute.Frames().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_lazy_modal_the_page_opens_holds_its_content_exactly_while_Open_is_true(bool open)
    {
        var modal = Ui.Modal.Lazy().Open(open)[Ui.Heading[Secret]];

        var html = modal.ToHtml();

        Assert.Equal(open, html.Contains(Secret, StringComparison.Ordinal));
        Assert.DoesNotContain("data-rask-modal-shown", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_modal_that_is_not_lazy_holds_its_content_while_closed()
    {
        var modal = Ui.Modal.Name("eager")[Ui.Heading[Secret]];

        var html = modal.ToHtml();

        Assert.Contains(Secret, html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-modal-shown", html, StringComparison.Ordinal);
    }
}
