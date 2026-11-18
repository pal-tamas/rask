using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiDialogButtons" />: a dialog's Cancel, which closes the modal it stands in, and its action, as Flux
///     draws a modal's footer.
/// </summary>
public partial class UiDialogButtonsTests : RaskMarkup
{
    [Fact]
    public void A_dialog_footer_is_a_ghost_cancel_then_the_primary_action_at_the_end()
    {
        var footer = Ui.DialogButtons.OnSave(() => { });

        var html = footer.ToHtml().AsText();

        Assert.StartsWith("<div class=\"[:where(&)]:flex [:where(&)]:gap-2\" data-ui-dialog-buttons><div class=\"flex-1\" data-ui-spacer=\"\"></div><div class=\"inline\" data-ui-modal-close>", html, StringComparison.Ordinal);
        Assert.Matches("<button [^>]*data-rask-key=\"cancel\" type=\"button\">Cancel</button></div>", html);
        Assert.Matches("<button [^>]*data-rask-key=\"save\"[^>]* type=\"button\">.*>Save</span></button></div>$", html);
        Assert.Contains(" bg-accent ", Tag(html, "<button class=", last: true), StringComparison.Ordinal);
        Assert.Contains(" bg-transparent ", Tag(html, "<button class="), StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"submit\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancel_closes_the_modal_it_stands_in_by_the_browsers_own_command()
    {
        var modal = Ui.Modal.Name("stock")[Ui.DialogButtons.OnSave(() => { })];

        var html = Page.Render(modal).Html;

        var id = Regex.Match(html, "<dialog id=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        var cancel = Tag(html[html.IndexOf("data-ui-dialog-buttons", StringComparison.Ordinal)..], "<button ");
        Assert.Equal("stock", id);
        Assert.Contains($"command=\"close\" commandfor=\"{id}\"", cancel, StringComparison.Ordinal);
        Assert.Contains("data-rask-key=\"cancel\"", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-click", cancel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_on_a_state_driven_modal_tells_the_page_it_closed()
    {
        var closed = 0;
        var page = Page.Render(() => [Ui.Modal.Open(true).OnClose(() => closed++)[Ui.DialogButtons.OnSave(() => { })]]);

        // The browser closes the dialog on the command; the dialog's close event is what reaches the page.
        var cancel = page.Find("[data-ui-dialog-buttons] [data-ui-modal-close] button");
        await page.On("dialog").Raise("close");

        Assert.Equal("close", cancel.Attribute("command"));
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Save_runs_the_handler_and_sends_no_form_it_stands_in()
    {
        var model = new Stock();
        var saved = 0;
        var submitted = 0;
        var page = Page.Render(() => [Form.Model(model).OnSubmit(_ => submitted++)[Ui.DialogButtons.OnSave(() => saved++)]]);

        await page.Click("Save");

        Assert.Equal(1, saved);
        Assert.Equal(0, submitted);
    }

    [Fact]
    public async Task Save_waits_on_an_async_handler_and_carries_the_spinner_the_runtime_shows_while_it_runs()
    {
        var booked = false;
        var page = Page.Render(() => [Ui.DialogButtons.OnSave(async () => { await Task.Yield(); booked = true; })]);

        var spinner = page.Exists("button[data-rask-key=\"save\"] [data-ui-loading-indicator]");
        await page.Click("Save");

        Assert.True(spinner);
        Assert.True(booked);
    }

    [Fact]
    public void The_dialog_names_its_own_buttons()
    {
        var footer = Ui.DialogButtons.OnSave(() => { }).Save("Book").Cancel("Back");

        var html = footer.ToHtml().AsText();

        Assert.Contains(">Back</button>", html, StringComparison.Ordinal);
        Assert.Contains(">Book</span></button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Unnamed_buttons_read_the_catalogs_words_in_english_and_hungarian()
    {
        var footer = () => Ui.DialogButtons.OnSave(() => { }).ToHtml().AsText();

        var english = KitCulture.In("en-US", footer);
        var hungarian = KitCulture.In("hu-HU", footer);

        Assert.Contains(">Cancel</button>", english, StringComparison.Ordinal);
        Assert.Contains(">Save</span></button>", english, StringComparison.Ordinal);
        Assert.Contains(">Mégse</button>", hungarian, StringComparison.Ordinal);
        Assert.Contains(">Mentés</span></button>", hungarian, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_page_adds_stands_after_the_spacer_and_before_cancel()
    {
        var footer = Ui.DialogButtons.OnSave(() => { })[Ui.Button.Key("draft")["Save as draft"]];

        var html = footer.ToHtml();

        Assert.True(html.IndexOf("data-ui-spacer", StringComparison.Ordinal) < html.IndexOf("Save as draft", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Save as draft", StringComparison.Ordinal) < html.IndexOf(">Cancel<", StringComparison.Ordinal));
    }

    [Fact]
    public void Submit_makes_the_action_the_forms_submit_and_leaves_cancel_a_plain_button()
    {
        var footer = Ui.DialogButtons.Submit().Save("Merge");

        var html = footer.ToHtml().AsText();

        Assert.Matches("<button [^>]*data-rask-key=\"save\" type=\"submit\">.*>Merge</span></button></div>$", html);
        Assert.Matches("<button [^>]*data-rask-key=\"cancel\" type=\"button\">Cancel</button>", html);
        Assert.DoesNotContain("data-rask-on-click", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "type=\"submit\"", RegexOptions.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Submit_sends_a_valid_form_and_its_handler_runs()
    {
        var model = new Merger();
        var merged = 0;
        var page = Page.Render(() => [Merging(model, () => merged++)]);

        await page.Type("Aminoff").Into("Name");
        await page.Click("Merge");

        Assert.Equal(1, merged);
    }

    [Fact]
    public async Task Submit_holds_back_an_invalid_form_and_its_handler_does_not_run()
    {
        var model = new Merger();
        var merged = 0;
        var refused = 0;
        var page = Page.Render(() => [Form.Model(model).OnSubmit(_ => merged++).OnInvalidSubmit(_ => refused++)[
            Ui.Input.Bind(() => model.Name).Label("Name"),
            Ui.DialogButtons.Submit().Save("Merge")
        ]]);

        await page.Click("Merge");

        Assert.Equal(0, merged);
        Assert.Equal(1, refused);
    }

    [Fact]
    public async Task Submit_carries_the_spinner_the_runtime_shows_while_the_forms_handler_runs()
    {
        var model = new Merger { Name = "Aminoff" };
        var merged = false;
        var page = Page.Render(() => [Merging(model, async () => { await Task.Yield(); merged = true; })]);

        var spinner = page.Exists("button[type=\"submit\"][data-rask-key=\"save\"] [data-ui-loading-indicator]");
        await page.Click("Merge");

        Assert.True(spinner);
        Assert.True(merged);
    }

    [Fact]
    public async Task Enter_in_a_field_submits_through_the_one_submit_button()
    {
        var model = new Merger { Name = "Aminoff" };
        var merged = 0;
        var page = Page.Render(() => [Merging(model, () => merged++)]);

        // Enter in a field submits the form by its first submit button; the dialog's row has only the action.
        var submits = page.FindAll("form button[type=\"submit\"]");
        await page.On("form").Submit();

        Assert.Single(submits);
        Assert.Equal("save", submits[0].Attribute("data-rask-key"));
        Assert.Equal(1, merged);
    }

    [Fact]
    public async Task Cancel_closes_the_modal_and_sends_no_form()
    {
        var model = new Merger { Name = "Aminoff" };
        var merged = 0;
        var closed = 0;
        var page = Page.Render(() => [Ui.Modal.Open(true).OnClose(() => closed++)[Merging(model, () => merged++)]]);

        var cancel = page.Find("[data-ui-dialog-buttons] [data-ui-modal-close] button");
        await page.On("dialog").Raise("close");

        Assert.Equal("close", cancel.Attribute("command"));
        Assert.Equal("button", cancel.Attribute("type"));
        Assert.Equal(1, closed);
        Assert.Equal(0, merged);
    }

    [Fact]
    public void Submit_beside_a_save_handler_names_both_and_says_which_to_drop()
    {
        var footer = Ui.DialogButtons.Submit().OnSave(() => { });

        var thrown = Assert.Throws<InvalidOperationException>(() => footer.ToHtml());

        Assert.Contains(".Submit()", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(".OnSave(", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Danger_draws_the_action_as_fluxs_danger_button_and_it_still_runs_its_handler()
    {
        var footer = Ui.DialogButtons.Danger().OnSave(() => { }).Save("Delete project");
        var flux = Ui.Button.Key("save").Danger.OnClick(() => { })["Delete project"];

        var html = footer.ToHtml().AsText();

        var action = Tag(html, "<button class=", last: true);
        Assert.Equal(Tag(flux.ToHtml().AsText(), "<button class="), action);
        Assert.Contains("bg-red-500", action, StringComparison.Ordinal);
        Assert.DoesNotContain(" bg-accent ", action, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", action, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Danger_with_submit_is_a_red_submit_that_sends_the_form()
    {
        var model = new Merger { Name = "Aminoff" };
        var removed = 0;
        var page = Page.Render(() => [Form.Model(model).OnSubmit(_ => removed++)[
            Ui.Input.Bind(() => model.Name).Label("Name"),
            Ui.DialogButtons.Submit().Danger().Save("Delete")
        ]]);

        var action = page.Find("button[data-rask-key=\"save\"]");
        await page.Click("Delete");

        Assert.Equal("submit", action.Attribute("type"));
        Assert.Contains("bg-red-500", action.Attribute("class"), StringComparison.Ordinal);
        Assert.Equal(1, removed);
    }

    private static Component Merging(Merger model, Action merge) =>
        Form.Model(model).OnSubmit(_ => merge())[
            Ui.Input.Bind(() => model.Name).Label("Name"),
            Ui.DialogButtons.Submit().Save("Merge")
        ];

    private static Component Merging(Merger model, Func<Task> merge) =>
        Form.Model(model).OnSubmit(_ => merge())[
            Ui.Input.Bind(() => model.Name).Label("Name"),
            Ui.DialogButtons.Submit().Save("Merge")
        ];

    private static string Tag(string html, string prefix, bool last = false)
    {
        var start = last ? html.LastIndexOf(prefix, StringComparison.Ordinal) : html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no tag starting {prefix}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }

    private sealed class Stock
    {
        public int Quantity { get; set; }
    }

    private sealed class Merger
    {
        [Required]
        public string? Name { get; set; }
    }
}
