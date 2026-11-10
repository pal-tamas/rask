using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

internal sealed class Entry
{
    [Required]
    public string Title { get; set; } = "Tank";
}

// A "save and add another" page: a submit that saves, a press that starts the form again.
internal sealed partial class EntryPage : Component
{
    internal readonly Entry Model = new();
    internal readonly EditContext Edit;
    internal int Saves;
    internal bool ResetAfterSave;

    public EntryPage() => Edit = new EditContext(Model);

    protected override Component? Render() =>
        Div[
            Form.Model(Model).Context(Edit).OnSubmit(Save)[f => [
                Input.Bind(() => Model.Title).Id("title"),
                Span.Id("unsaved")[f.Unsaved ? "unsaved" : "saved"],
            ]],
            Button.Id("start-again").OnClick(() => Edit.Reset())["Start again"]
        ];

    private void Save(Entry entry)
    {
        Saves++;
        if (ResetAfterSave)
        {
            Edit.Reset();
        }
    }
}

// What the server tells the page about a form's unsaved values: `f.Unsaved` for the children, and the counts that
// let the browser take its `data-dirty` marks away (rask-dirty.ts, driven by DirtyMarkClientTests).
public partial class FormUnsavedTests : global::Rask.Core.RaskMarkup
{
    private static string Said(Page page) => page.TextOf("#unsaved");

    [Fact]
    public void A_form_nobody_changed_says_it_holds_nothing_unsaved()
    {
        var page = Page.Render(new EntryPage());

        var said = Said(page);

        Assert.Equal("saved", said);
        Assert.Null(page.Attr("data-rask-saved"));
    }

    [Fact]
    public async Task A_value_that_arrived_makes_the_form_unsaved_and_the_value_it_had_makes_it_saved_again()
    {
        var page = Page.Render(new EntryPage());

        await page.On("#title").Change("Barrel");
        var changed = Said(page);
        await page.On("#title").Change("Tank");

        Assert.Equal("unsaved", changed);
        Assert.Equal("saved", Said(page));
    }

    [Fact]
    public async Task An_accepted_submit_saves_what_the_fields_hold_and_tells_the_browser()
    {
        var entry = new EntryPage();
        var page = Page.Render(entry);
        await page.On("#title").Change("Barrel");

        await page.On("form").Submit();

        Assert.Equal(1, entry.Saves);
        Assert.Equal("saved", Said(page));
        Assert.Equal("1", page.Attr("data-rask-saved"));
        Assert.False(entry.Edit.IsModifiedAny);
    }

    [Fact]
    public async Task A_submit_the_rules_refuse_leaves_the_form_unsaved()
    {
        var entry = new EntryPage();
        var page = Page.Render(entry);
        await page.On("#title").Change("");

        await page.On("form").Submit();

        Assert.Equal(0, entry.Saves);
        Assert.Equal("unsaved", Said(page));
        Assert.Null(page.Attr("data-rask-saved"));
    }

    [Fact]
    public async Task Starting_again_puts_the_value_back_clears_the_message_and_tells_the_browser()
    {
        var entry = new EntryPage();
        var page = Page.Render(entry);
        await page.On("#title").Change("");
        await page.On("form").Submit();

        await page.On("#start-again").Click();

        Assert.Equal("Tank", entry.Model.Title);
        Assert.Equal("saved", Said(page));
        Assert.Equal("1", page.Attr("data-rask-settled"));
        Assert.False(entry.Edit.HasValidationMessages());
        Assert.Contains("value=\"Tank\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_and_add_another_starts_again_from_the_values_first_drawn()
    {
        var entry = new EntryPage { ResetAfterSave = true };
        var page = Page.Render(entry);
        await page.On("#title").Change("Barrel");

        await page.On("form").Submit();

        Assert.Equal(1, entry.Saves);
        Assert.Equal("Tank", entry.Model.Title);
        Assert.Equal("saved", Said(page));
        Assert.Equal("1", page.Attr("data-rask-settled"));
    }
}
