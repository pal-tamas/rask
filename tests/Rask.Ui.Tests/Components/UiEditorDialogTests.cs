using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A list of drivers with its editor over it, opened by <c>?edit=</c>: the shape of both apps' list pages.</summary>
[Route("/ui-editor-dialog/drivers")]
public sealed partial class EditorDialogDriversPage(RouteState route) : Component
{
    [QueryParam("edit")] public string? Edit { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))],
            A.Id("new").Href(route.With("edit", UiEditorDialog.New))["New driver"],
            Button.Id("open-7").OnClick(() => Go.With("edit", "7").Pushing())["Edit 7"],
            Ui.EditorDialog.In("edit").Heading(Edit == UiEditorDialog.New ? "New driver" : "Edit driver")[
                P.Id("editing")["Editing " + Edit]
            ]
        ];
}

/// <summary>The same list with a form that has rows of its own: the wide sheet.</summary>
[Route("/ui-editor-dialog/vehicles")]
public sealed partial class EditorDialogVehiclesPage : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Main[Ui.EditorDialog.In("edit").Wide()[P["Vehicle"]]];
}

/// <summary><c>Ui.EditorDialog.In("edit")</c>: the editor over its list, opened and closed by the list's own address.</summary>
public sealed partial class UiEditorDialogTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void With_no_key_in_the_address_the_dialog_is_closed_and_holds_nothing()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?q=ada");

        var dialog = page.Find("dialog");

        Assert.Equal("false", dialog.Attribute("data-rask-modal-open"));
        Assert.False(page.Exists("#editing"));
        Assert.DoesNotContain("Edit driver", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_key_opens_it_over_the_list_with_its_heading_and_content()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?edit=new");

        var dialog = page.Find("dialog");

        Assert.Equal("true", dialog.Attribute("data-rask-modal-open"));
        Assert.Equal("New driver", page.TextOf("dialog h2"));
        Assert.Equal("Editing new", page.TextOf("#editing"));
    }

    [Fact]
    public void The_link_that_opens_it_keeps_the_filters_the_list_stands_at()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?q=ada&page=2");

        var href = page.Find("#new").Attribute("href");

        Assert.Equal("/ui-editor-dialog/drivers?q=ada&page=2&edit=new", href);
    }

    [Fact]
    public async Task A_move_of_the_address_to_a_record_opens_it_with_that_record()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?q=ada");

        await page.On("#open-7").Click();

        Assert.Equal("true", page.Find("dialog").Attribute("data-rask-modal-open"));
        Assert.Equal("Edit driver", page.TextOf("dialog h2"));
        Assert.Equal("Editing 7", page.TextOf("#editing"));
    }

    [Fact]
    public async Task Closing_it_takes_its_key_out_and_keeps_the_rest_of_the_address()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?q=ada&page=2&edit=7");

        await page.On("dialog").Raise("close");

        Assert.Equal("page=2&q=ada", page.TextOf("#at"));
        Assert.Equal("false", page.Find("dialog").Attribute("data-rask-modal-open"));
        Assert.False(page.Exists("#editing"));
    }

    [Fact]
    public async Task A_close_after_Back_took_the_key_out_moves_nothing()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?q=ada");

        await page.On("dialog").Raise("close");

        Assert.Equal("q=ada", page.TextOf("#at"));
    }

    [Fact]
    public void A_small_form_is_a_centred_box()
    {
        var page = Page.Visit("/ui-editor-dialog/drivers?edit=7");

        var dialog = page.Find("dialog");

        Assert.True(dialog.HasClass("sm:min-w-[28rem]"));
        Assert.False(dialog.Attributes.ContainsKey("data-ui-flyout"));
    }

    [Fact]
    public void A_wide_one_is_a_sheet_from_the_right_as_wide_as_the_screen_allows()
    {
        var page = Page.Visit("/ui-editor-dialog/vehicles?edit=3");

        var dialog = page.Find("dialog");

        Assert.True(dialog.HasClass("md:w-[min(84rem,100vw)]"));
        Assert.True(dialog.Attributes.ContainsKey("data-ui-flyout"));
        Assert.True(dialog.HasClass("[:where(&)]:ms-auto"));
    }
}
