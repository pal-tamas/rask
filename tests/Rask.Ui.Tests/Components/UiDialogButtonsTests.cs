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
}
