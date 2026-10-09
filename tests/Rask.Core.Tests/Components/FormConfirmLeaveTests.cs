using System.ComponentModel.DataAnnotations;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

internal sealed class Draft
{
    [Required]
    public string Title { get; set; } = "";
}

// A form that guards its edits, with a save that can be made to fail.
internal sealed partial class DraftPage : Component
{
    internal readonly Draft Model = new();
    internal bool SaveFails;
    internal int Saves;

    protected override Component? Render() =>
        Form.Model(Model).OnSubmit(Save).ConfirmLeave("Leave without saving?")[
            Input.Bind(() => Model.Title).Id("title")
        ];

    private void Save(Draft draft)
    {
        if (SaveFails)
        {
            throw new InvalidOperationException("the store is down");
        }

        Saves++;
    }
}

// What the server renders for `ConfirmLeave`. Whether the form is unsaved is decided in the browser
// (rask-leave.ts, driven by ConfirmLeaveHookTests in Rask.Server.E2E.Tests); what is held here is the half
// only the server knows — that a submit was accepted — and that it is told to the browser by `data-rask-saved`
// changing.
public partial class FormConfirmLeaveTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_form_told_to_confirm_leaving_carries_the_message_for_the_browser()
    {
        var form = Form.Model(new Draft()).ConfirmLeave("Leave <without> saving?");

        var html = form.ToHtml();

        Assert.Equal("<form data-rask-confirm-leave=\"Leave &lt;without&gt; saving?\"></form>", html);
    }

    [Fact]
    public async Task A_form_never_told_to_confirm_leaving_carries_no_guard_even_after_a_save()
    {
        var model = new Draft { Title = "Notes" };
        var page = Page.Render(() => Form.Model(model).OnSubmit(_ => { })[Input.Bind(() => model.Title)]);

        await page.On("form").Submit();

        Assert.Null(page.Attr("data-rask-confirm-leave"));
        Assert.Null(page.Attr("data-rask-saved"));
    }

    [Fact]
    public async Task A_guarded_form_nobody_has_submitted_carries_no_saved_mark()
    {
        var page = Page.Render(new DraftPage());

        await page.On("#title").Change("Notes");

        Assert.Equal("Leave without saving?", page.Attr("data-rask-confirm-leave"));
        Assert.Null(page.Attr("data-rask-saved"));
    }

    [Fact]
    public async Task Each_accepted_submit_changes_the_saved_mark_the_browser_reads()
    {
        var draft = new DraftPage();
        var page = Page.Render(draft);
        await page.On("#title").Change("Notes");

        await page.On("form").Submit();
        var afterTheFirst = page.Attr("data-rask-saved");
        await page.On("form").Submit();

        Assert.Equal(2, draft.Saves);
        Assert.Equal("1", afterTheFirst);
        Assert.Equal("2", page.Attr("data-rask-saved"));
    }

    [Fact]
    public async Task A_submit_validation_refuses_leaves_the_saved_mark_as_it_was()
    {
        var draft = new DraftPage();
        var page = Page.Render(draft);

        await page.On("form").Submit();

        Assert.Equal(0, draft.Saves);
        Assert.Null(page.Attr("data-rask-saved"));
    }

    [Fact]
    public async Task A_submit_whose_handler_throws_leaves_the_saved_mark_as_it_was()
    {
        var draft = new DraftPage { SaveFails = true };
        var page = Page.Render(draft);
        await page.On("#title").Change("Notes");

        await page.On("form").Submit();

        Assert.Equal("Notes", draft.Model.Title);
        Assert.Null(page.Attr("data-rask-saved"));
    }
}
