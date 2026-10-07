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
        Assert.Contains("popover=\"auto\"", dialog);
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
        // What a browser without invoker commands acts on instead.
        Assert.Contains("popovertarget=\"confirm\"", Tag(html, "<button"));
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
        var wrapped = Ui.ModalTrigger.Name("confirm")[Ui.Tooltip.Tip("Opens a dialog")
[Button.Type(ButtonType.Button)["Delete"]]];

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
    [InlineData(null, null, "any", "auto")]
    [InlineData(false, null, "closerequest", "manual")]
    [InlineData(false, false, "none", "manual")]
    // The platform has no "a click outside, but not Escape": a dialog that is not escapable gives both up.
    [InlineData(null, false, "none", "manual")]
    public void Dismissible_and_escapable_choose_what_the_browser_lets_close_it(
        bool? dismissible, bool? escapable, string closedBy, string fallback)
    {
        var modal = Ui.Modal.Name("confirm").Dismissible(dismissible).Escapable(escapable);

        var dialog = Tag(modal.ToHtml(), "<dialog");

        Assert.Contains($"closedby=\"{closedBy}\"", dialog);
        Assert.Contains($"popover=\"{fallback}\"", dialog);
    }

    [Theory]
    [InlineData(null, "ms-auto", " border-s ")]
    [InlineData(Ui.ModalPosition.Right, "ms-auto", " border-s ")]
    [InlineData(Ui.ModalPosition.Left, "me-auto", " border-e ")]
    [InlineData(Ui.ModalPosition.Bottom, "mt-auto", " border-t ")]
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

        Assert.Contains("my-2 me-2 ms-auto", dialog);
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
        // button behind the panel, because the browser counts the whole layer as inside the dialog.
        Assert.Contains("overflow-y-auto", Tag(html, "<dialog"));
        Assert.DoesNotContain("md:w-lg", Tag(html, "<dialog"));
        Assert.Contains("md:w-lg", html);
        Assert.Contains("closedby=\"closerequest\"", html);
        Assert.Contains("aria-hidden=\"true\" command=\"close\"", html);
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
        Assert.Contains("popovertargetaction=\"hide\"", Tag(html, "<button"));
    }

    [Fact]
    public void Closing_a_named_modal_is_heard_through_the_dialogs_own_events()
    {
        // Handler ids are only written by a live render.
        var silent = RenderedPage.Render(Ui.Modal.Name("confirm")).Html;

        var heard = RenderedPage.Render(Ui.Modal.Name("confirm").OnClose(() => { }).OnCancel(() => { })).Html;

        Assert.DoesNotContain("data-rask-on-", Tag(silent, "<dialog"));
        Assert.Contains("data-rask-on-toggle=", Tag(heard, "<dialog"));
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
    public void Open_hands_the_state_to_the_page()
    {
        var open = Tag(Ui.Modal.Name("confirm").Open(true).ToHtml(), "<dialog");

        var closed = Tag(Ui.Modal.Name("confirm").Open(false).ToHtml(), "<dialog");

        // Not a popover and not named for a command: nothing but the page's own render opens it.
        Assert.DoesNotContain("popover", open);
        Assert.DoesNotContain("id=", open);
        Assert.True(IsOpen(open));
        Assert.Contains("data-rask-focus-trap", open);
        Assert.False(IsOpen(closed));
        Assert.DoesNotContain("data-rask-focus-trap", closed);
    }

    [Fact]
    public void A_modal_with_no_name_is_rendered_open_by_the_page_that_renders_it()
    {
        var html = Ui.Modal.OnClose(() => { })[P["Details"]].ToHtml();

        Assert.True(IsOpen(Tag(html, "<dialog")));
    }

    [Fact]
    public async Task A_dismissal_on_the_state_driven_path_raises_OnCancel_then_OnClose()
    {
        var heard = new List<string>();
        var page = RenderedPage.Render(Ui.Modal
            .Open(true)
            .OnCancel(() => heard.Add("cancel"))
            .OnClose(() => heard.Add("close")));

        // The backdrop the kit draws, then the control the runtime presses on Escape.
        await page.On("[data-ui-modal] > button").Click();
        await page.On("[data-rask-dismiss]").Click();

        Assert.Equal(["cancel", "close", "cancel", "close"], heard);
    }

    [Fact]
    public async Task The_corner_button_and_a_modal_close_are_closes_not_dismissals()
    {
        var heard = new List<string>();
        var page = RenderedPage.Render(Ui.Modal
            .Open(true)
            .OnCancel(() => heard.Add("cancel"))
            .OnClose(() => heard.Add("close"))[
            Ui.ModalClose[Button.Type(ButtonType.Button).Id("cancel")["Cancel"]]
        ]);

        await page.On("[aria-label='Close modal']").Click();
        await page.On("#cancel").Click();

        Assert.Equal(["close", "close"], heard);
    }

    [Fact]
    public void A_state_driven_modal_that_is_not_dismissible_or_escapable_has_neither_way_out()
    {
        var modal = Ui.Modal.Open(true).Dismissible(false).Escapable(false).OnClose(() => { });

        var html = RenderedPage.Render(modal).Html;

        // The backdrop is still drawn; it is just not a button any more.
        Assert.Contains("<div class=\"fixed inset-0 z-50 bg-black/10\" aria-hidden=\"true\">", html);
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
