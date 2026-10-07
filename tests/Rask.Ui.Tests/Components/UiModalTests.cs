using Rask.Core;
using RenderedPage = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Modal</c>, <c>Ui.ModalTrigger</c> and <c>Ui.ModalClose</c>: Flux's modal as markup the browser runs.
/// </summary>
/// <remarks>
///     What it looks like, and what it does once open, is held to Flux's live page by
///     <c>scripts/flux/parity-modal.mjs</c>. These hold the contract that script cannot see: which attribute
///     each prop writes, and what a callback hears.
/// </remarks>
public partial class UiModalTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_named_modal_is_a_dialog_the_browser_opens()
    {
        var html = Ui.Modal.Name("confirm")[P["Sure?"]].ToHtml();

        var dialog = Tag(html, "<dialog");

        Assert.StartsWith("<div class=\"inline\" data-ui-modal>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"confirm\"", dialog);
        Assert.Contains("data-modal=\"confirm\"", dialog);
        // What Flux does in script, asked of the runtime: both dismissals, and the page held still behind it.
        Assert.Contains("data-rask-modal=\"any\"", dialog);
        Assert.Contains("data-rask-lock=\"scroll\"", dialog);
        Assert.DoesNotContain("data-rask-modal-open", dialog);
        Assert.DoesNotContain("popover", dialog);
        Assert.DoesNotContain("closedby", dialog);
        Assert.False(IsOpen(dialog));
    }

    [Fact]
    public void The_trigger_makes_its_button_an_invoker_for_the_named_dialog()
    {
        var trigger = Ui.ModalTrigger.Name("confirm")[Button.Type(ButtonType.Button)["Delete"]];

        var html = trigger.ToHtml();

        Assert.StartsWith("<div class=\"contents\" data-ui-modal-trigger>", html, StringComparison.Ordinal);
        Assert.Contains("command=\"show-modal\"", Tag(html, "<button"));
        Assert.Contains("commandfor=\"confirm\"", Tag(html, "<button"));
        // An engine without invoker commands gets them from the runtime, so nothing stands in for them.
        Assert.DoesNotContain("popovertarget", html);
    }

    [Fact]
    public void The_trigger_wires_the_kits_own_button_and_keeps_what_it_carried()
    {
        var button = Ui.Button.Attributes(("data-testid", "open"))["Edit profile"];

        var html = Ui.ModalTrigger.Name("edit-profile")[button].ToHtml();

        Assert.Contains("data-testid=\"open\"", html);
        Assert.Contains("command=\"show-modal\"", html);
        Assert.Contains("commandfor=\"edit-profile\"", html);
    }

    [Fact]
    public void The_trigger_finds_the_button_inside_whatever_wraps_it()
    {
        var wrapped = Ui.ModalTrigger.Name("confirm")[Ui.Tooltip.Content("Opens a dialog")[Button.Type(ButtonType.Button)["Delete"]]];

        var html = wrapped.ToHtml();

        Assert.Contains("commandfor=\"confirm\"", html);
    }

    [Fact]
    public void A_trigger_with_nothing_to_press_says_what_to_write()
    {
        var empty = Ui.ModalTrigger.Name("confirm")["Delete"];

        var failure = Assert.Throws<InvalidOperationException>(empty.ToHtml);

        Assert.Contains("Ui.ModalTrigger.Name(\"confirm\")[Ui.Button[\"Open\"]]", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shortcut_goes_on_the_button_so_the_runtime_presses_it()
    {
        var trigger = Ui.ModalTrigger.Name("search").Shortcut("mod+k")[Button.Type(ButtonType.Button)["Search"]];

        var html = trigger.ToHtml();

        Assert.Contains("data-rask-shortcut=\"mod+k\"", System.Net.WebUtility.HtmlDecode(Tag(html, "<button")));
    }

    [Fact]
    public void The_corner_button_is_named_and_closes_the_dialog_by_command()
    {
        var html = Ui.Modal.Name("confirm")[P["Sure?"]].ToHtml();

        var close = Tag(html, "<button");

        Assert.Contains("data-ui-modal-close", html);
        Assert.Contains("aria-label=\"Close modal\"", close);
        Assert.Contains("command=\"close\"", close);
        Assert.Contains("commandfor=\"confirm\"", close);
    }

    [Fact]
    public void Not_closable_leaves_the_corner_button_out()
    {
        var html = Ui.Modal.Name("confirm").Closable(false)[P["Sure?"]].ToHtml();

        Assert.DoesNotContain("<button", html);
        Assert.DoesNotContain("data-ui-modal-close", html);
    }

    [Theory]
    [InlineData(null, null, "any")]
    [InlineData(true, true, "any")]
    [InlineData(false, null, "escape")]
    [InlineData(false, false, "none")]
    // The platform's `closedby` cannot say "a click outside, but not Escape"; the runtime's attribute can.
    [InlineData(null, false, "press")]
    public void Dismissible_and_escapable_choose_which_dismissals_the_runtime_allows(
        bool? dismissible, bool? escapable, string dismissedBy)
    {
        var modal = Ui.Modal.Name("confirm").Dismissible(dismissible).Escapable(escapable);

        var dialog = Tag(modal.ToHtml(), "<dialog");

        Assert.Contains($"data-rask-modal=\"{dismissedBy}\"", dialog);
        Assert.DoesNotContain("closedby", dialog);
    }

    [Theory]
    [InlineData(null, "m-0 ms-auto", " border-s ")]
    [InlineData(Ui.ModalPosition.Right, "m-0 ms-auto", " border-s ")]
    [InlineData(Ui.ModalPosition.Left, "m-0 me-auto", " border-e ")]
    [InlineData(Ui.ModalPosition.Bottom, "m-0 mt-auto", " border-t ")]
    public void A_flyout_sits_against_one_edge_with_its_line_on_the_page_side(
        Ui.ModalPosition? position, string margin, string line)
    {
        var flyout = Ui.Modal.Name("edit").Flyout().Position(position);

        var dialog = Tag(flyout.ToHtml(), "<dialog");

        Assert.Contains("data-ui-flyout", dialog);
        Assert.Contains(margin, dialog);
        Assert.Contains(line, dialog);
        Assert.DoesNotContain("rounded-xl", dialog);
    }

    [Fact]
    public void The_legacy_flyout_variant_is_the_flyout()
    {
        var legacy = Ui.Modal.Name("edit").Variant(Ui.ModalVariant.Flyout);

        var dialog = Tag(legacy.ToHtml(), "<dialog");

        Assert.Equal(Tag(Ui.Modal.Name("edit").Flyout().ToHtml(), "<dialog"), dialog);
    }

    [Fact]
    public void A_floating_flyout_stands_off_the_edges_as_a_panel()
    {
        var floating = Ui.Modal.Name("edit").Flyout().Floating;

        var dialog = Tag(floating.ToHtml(), "<dialog");

        Assert.Contains("m-2 ms-auto", dialog);
        Assert.Contains("rounded-xl", dialog);
        Assert.Contains("shadow-lg", dialog);
        Assert.DoesNotContain(" border-s ", dialog);
    }

    [Fact]
    public void The_bare_variant_draws_no_panel_and_no_close_button()
    {
        var bare = Ui.Modal.Name("search").Bare[P["palette"]];

        var html = bare.ToHtml();

        Assert.Contains("bg-transparent", Tag(html, "<dialog"));
        Assert.DoesNotContain("shadow-lg", html);
        Assert.DoesNotContain("<button", html);
    }

    [Fact]
    public void Scrolling_the_body_puts_the_panel_inside_a_layer_that_scrolls()
    {
        var terms = Ui.Modal.Name("terms").Scroll(Ui.ModalScroll.Body).Class("md:w-lg")[P["Long."]];

        var html = terms.ToHtml();

        // The dialog is the layer, so the caller's width is the panel's, and a click outside lands on a
        // button behind the panel — the whole layer is inside the dialog's box — which asks the dialog to
        // close as a dismissal: cancel, then close.
        Assert.Contains("overflow-y-auto", Tag(html, "<dialog"));
        Assert.DoesNotContain("md:w-lg", Tag(html, "<dialog"));
        Assert.Contains("md:w-lg", html);
        Assert.Contains("aria-hidden=\"true\" command=\"request-close\" commandfor=\"terms\"", html);
    }

    [Fact]
    public void A_class_from_the_call_site_joins_the_dialogs_own()
    {
        var html = Ui.Modal.Name("edit").Class("md:w-96").ToHtml();

        Assert.Contains("md:w-96", Tag(html, "<dialog"));
        Assert.Contains("rounded-xl", Tag(html, "<dialog"));
    }

    [Fact]
    public void The_focus_placeholder_is_first_and_takes_the_focus_a_field_would_get()
    {
        var html = Ui.Modal.Name("edit")[Input.Value("Ada")].ToHtml();

        var inside = html[(html.IndexOf('>', html.IndexOf("<dialog", StringComparison.Ordinal)) + 1)..];

        Assert.StartsWith("<div tabindex=\"-1\" data-ui-focus-placeholder autofocus></div>", inside, StringComparison.Ordinal);
    }

    [Fact]
    public void A_modal_close_makes_its_button_a_close_command_for_the_modal_it_is_in()
    {
        var modal = Ui.Modal.Name("confirm").Closable(false)[Ui.ModalClose[Button.Type(ButtonType.Button)["Cancel"]]];

        var html = modal.ToHtml();

        Assert.Contains("<div class=\"inline\" data-ui-modal-close>", html);
        Assert.Contains("command=\"close\"", Tag(html, "<button"));
        Assert.Contains("commandfor=\"confirm\"", Tag(html, "<button"));
        Assert.DoesNotContain("popovertarget", html);
    }

    [Fact]
    public void Closing_a_named_modal_is_heard_through_the_dialogs_own_events()
    {
        // Handler ids are only written by a live render.
        var silent = RenderedPage.Render(Ui.Modal.Name("confirm")).Html;

        var heard = RenderedPage.Render(Ui.Modal.Name("confirm").OnClose(() => { }).OnCancel(() => { })).Html;

        Assert.DoesNotContain("data-rask-on-", Tag(silent, "<dialog"));
        Assert.Contains("data-rask-on-close=", Tag(heard, "<dialog"));
        Assert.Contains("data-rask-on-cancel=", Tag(heard, "<dialog"));
    }

    [Fact]
    public async Task The_platforms_cancel_reaches_OnCancel_on_a_named_modal()
    {
        var cancelled = 0;
        var page = RenderedPage.Render(Ui.Modal.Name("edit").OnCancel(() => cancelled++));

        await page.On("dialog").Raise("cancel");

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Open_hands_the_state_to_the_page_and_the_runtime_shows_it_as_a_modal()
    {
        var open = Tag(Ui.Modal.Name("confirm").Open(true).ToHtml(), "<dialog");

        var closed = Tag(Ui.Modal.Name("confirm").Open(false).ToHtml(), "<dialog");

        // Never the `open` attribute, which shows a dialog without the top layer or a backdrop.
        Assert.Contains("data-rask-modal-open=\"true\"", open);
        Assert.False(IsOpen(open));
        Assert.Contains("data-rask-modal-open=\"false\"", closed);
        Assert.False(IsOpen(closed));
        Assert.DoesNotContain("data-rask-focus-trap", open);
        Assert.DoesNotContain("data-open", open);
    }

    [Fact]
    public void A_modal_with_no_name_is_open_while_the_page_renders_it()
    {
        var html = Ui.Modal.OnClose(() => { })[P["Details"]].ToHtml();

        var dialog = Tag(html, "<dialog");

        Assert.Contains("data-rask-modal-open=\"true\"", dialog);
        Assert.Matches("id=\"ui-modal-\\d+\"", dialog);
    }

    [Fact]
    public async Task A_dismissal_on_the_state_driven_path_raises_OnCancel_then_OnClose()
    {
        var heard = new List<string>();
        var page = RenderedPage.Render(Ui.Modal
            .Open(true)
            .OnCancel(() => heard.Add("cancel"))
            .OnClose(() => heard.Add("close")));

        // What the dialog raises for Escape, and what the runtime raises on it for a press outside.
        await page.On("dialog").Raise("cancel");
        await page.On("dialog").Raise("close");

        Assert.Equal(["cancel", "close"], heard);
    }

    [Fact]
    public void The_corner_button_and_a_modal_close_close_a_state_driven_modal_by_command_too()
    {
        var modal = Ui.Modal.Open(true).OnClose(() => { })[
            Ui.ModalClose[Button.Type(ButtonType.Button).Id("cancel")["Cancel"]]
        ];

        var html = RenderedPage.Render(modal).Html;

        // The browser closes it and the dialog's close event tells the page: no click handler on either.
        var id = System.Text.RegularExpressions.Regex.Match(html, "<dialog id=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        var corner = Tag(html, "<button class=");
        var cancel = Tag(html, "<button id=\"cancel\"");
        Assert.Contains($"command=\"close\" commandfor=\"{id}\"", corner);
        Assert.Contains($"command=\"close\" commandfor=\"{id}\"", cancel);
        Assert.DoesNotContain("data-rask-on-click", corner);
        Assert.DoesNotContain("data-rask-on-click", cancel);
    }

    [Fact]
    public void A_state_driven_modal_draws_no_backdrop_or_dismiss_control_of_its_own()
    {
        var modal = Ui.Modal.Open(true).Dismissible(false).Escapable(false).OnClose(() => { });

        var html = RenderedPage.Render(modal).Html;

        // The top layer draws ::backdrop, and the runtime holds both dismissals back.
        Assert.Contains("data-rask-modal=\"none\"", Tag(html, "<dialog"));
        Assert.DoesNotContain("bg-black/10", html);
        Assert.DoesNotContain("data-rask-dismiss", html);
    }

    // The `open` attribute, which a class naming the `open:` variant is not.
    private static bool IsOpen(string dialog) =>
        System.Text.RegularExpressions.Regex.IsMatch(dialog, @"\sopen(=""[^""]*"")?[\s>]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    // The opening tag that starts with `prefix`, so an assertion about one element cannot pass on another's.
    private static string Tag(string html, string prefix)
    {
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no tag starting {prefix}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }
}
