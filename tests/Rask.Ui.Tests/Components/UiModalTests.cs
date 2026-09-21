namespace Rask.Ui.Tests.Components;

/// <summary>
///     The dialog: a real <c>&lt;dialog&gt;</c>, a popover by default.
/// </summary>
public partial class UiModalTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_is_a_native_dialog_element_on_both_paths()
    {
        // Not a div wearing a role. A <dialog> is the element the platform means, and it is what lets
        // the popover path get a real ::backdrop rather than one painted by the kit.
        Assert.Contains("<dialog", Popover());
        Assert.Contains("<dialog", UiModal.Title("Delete order").Open(true).ToHtml());
    }

    [Fact]
    public void An_id_with_no_state_makes_it_a_popover()
    {
        // The default, and the better one: the browser supplies the top layer, Escape, light-dismiss
        // and the backdrop, none of it implemented here and none of it needing a runtime.
        var html = Popover();

        Assert.Contains("popover=\"auto\"", html);
        Assert.Contains("id=\"confirm\"", html);
    }

    [Fact]
    public void The_trigger_names_the_dialog_rather_than_running_a_handler() =>
        Assert.Contains("popovertarget=\"confirm\"", Popover());

    [Fact]
    public void The_close_control_on_the_popover_path_is_markup_not_a_handler()
    {
        // popovertargetaction=hide is what closes it, so the header button works with scripting off.
        Assert.Contains("popovertargetaction=\"hide\"", Popover());
    }

    [Fact]
    public void No_trigger_still_renders_the_dialog_so_something_else_can_open_it()
    {
        var html = UiModal.Title("Delete order").Id("confirm").ToHtml();

        Assert.Contains("popover=\"auto\"", html);
        Assert.DoesNotContain("<button class=\"btn\" popovertarget", html);
    }

    [Fact]
    public void Setting_Open_takes_it_off_the_popover_path()
    {
        // They cannot coexist: a [popover] element is display:none until the browser shows it, so a
        // modal-open class on one would be a class that changes nothing.
        var html = UiModal.Title("Delete order").Id("confirm").Open(true).ToHtml();

        Assert.DoesNotContain("popover=", html);
        Assert.Contains("modal-open", html);
        Assert.Contains("open", html);
    }

    [Fact]
    public void The_state_driven_path_can_be_kept_mounted_but_hidden() =>
        Assert.DoesNotContain("modal-open", UiModal.Title("Delete order").Open(false).ToHtml());

    [Fact]
    public void Without_a_stated_placement_it_is_a_sheet_on_a_phone_and_centred_above_it()
    {
        // Not a stylistic default: a centred dialog on a 360px screen either overflows or shrinks its
        // content past readable, and a stack trace is the one thing here that must stay readable.
        var html = Popover();

        Assert.Contains("modal-bottom", html);
        Assert.Contains("sm:modal-middle", html);
    }

    [Theory]
    [InlineData(UiModalPosition.Top, "modal-top")]
    [InlineData(UiModalPosition.Middle, "modal-middle")]
    [InlineData(UiModalPosition.Bottom, "modal-bottom")]
    [InlineData(UiModalPosition.Start, "modal-start")]
    [InlineData(UiModalPosition.End, "modal-end")]
    public void Every_position_writes_its_own_class(UiModalPosition position, string expected) =>
        Assert.Contains(expected,
            UiModal.Title("Delete order").Id("confirm").Position(position).ToHtml());

    [Fact]
    public void A_stated_position_replaces_the_responsive_default_rather_than_fighting_it()
    {
        // Appending the default would leave `sm:modal-middle` overriding the caller's choice at every
        // width above a phone — the class present, and ignored.
        var html = UiModal.Title("Delete order").Id("confirm").Position(UiModalPosition.Top).ToHtml();

        Assert.DoesNotContain("sm:modal-middle", html);
        Assert.DoesNotContain("modal-bottom", html);
    }

    [Fact]
    public void Both_paths_close_on_a_click_outside_through_the_backdrop_button()
    {
        // A MODAL dialog has no light-dismiss of its own in most browsers: the viewport-sized .modal IS the
        // dialog, so a click on the dimmed area lands inside it. daisyUI's backdrop button is what it lands on.
        Assert.Contains("modal-backdrop", Popover());
        Assert.Contains("modal-backdrop",
            UiModal.Title("Delete order").Open(true).OnClose(() => { }).ToHtml());
    }

    [Fact]
    public void The_trigger_opens_it_modally_and_falls_back_to_the_popover()
    {
        // The invoker command is what makes it a real modal — the page behind inert, focus contained and
        // handed back — with no script. The popover attribute beside it is for a browser without invokers.
        var trigger = Tag(Popover(), "<button class=\"btn\"");

        Assert.Contains("command=\"show-modal\"", trigger);
        Assert.Contains("commandfor=\"confirm\"", trigger);
        Assert.Contains("popovertarget=\"confirm\"", trigger);
    }

    [Fact]
    public void The_close_button_and_the_backdrop_close_it_both_ways()
    {
        var html = Popover();

        foreach (var control in new[] { Tag(html, "<button class=\"btn btn-ghost"), Tag(html, "<button class=\"modal-backdrop") })
        {
            Assert.Contains("command=\"close\"", control);
            Assert.Contains("commandfor=\"confirm\"", control);
            Assert.Contains("popovertargetaction=\"hide\"", control);
        }
    }

    [Fact]
    public void Not_dismissible_drops_the_backdrop_and_the_fallbacks_light_dismiss()
    {
        var html = UiModal.Title("Unsaved work").Id("confirm").Dismissible(false).ToHtml();

        Assert.DoesNotContain("modal-backdrop", html);
        Assert.Contains("popover=\"manual\"", html);
        // The close button is still there: not dismissible is not inescapable.
        Assert.Contains("command=\"close\"", html);
    }

    [Fact]
    public void Not_escapable_asks_the_browser_to_ignore_close_requests()
    {
        var html = UiModal.Title("Unsaved work").Id("confirm").Escapable(false).ToHtml();

        Assert.Contains("closedby=\"none\"", html);
        Assert.Contains("popover=\"manual\"", html);
    }

    [Fact]
    public void Not_closable_drops_the_header_close_button()
    {
        var html = UiModal.Title("Terms").Id("confirm").Closable(false).ToHtml();

        Assert.DoesNotContain("btn-square", html);
        // Still dismissible by a click outside unless that is turned off too.
        Assert.Contains("modal-backdrop", html);
    }

    [Fact]
    public void The_state_driven_path_traps_focus_only_while_it_is_open()
    {
        // The runtime's trap gives it containment, Escape and focus handed back; on a dialog kept mounted while
        // closed, the attribute's removal is what hands focus back.
        var open = UiModal.Title("Delete order").Open(true).OnClose(() => { }).ToHtml();
        Assert.Contains("data-rask-focus-trap", open);
        Assert.Contains("tabindex=\"-1\"", Tag(open, "<dialog"));

        Assert.DoesNotContain("data-rask-focus-trap", UiModal.Title("Delete order").Open(false).OnClose(() => { }).ToHtml());
    }

    [Fact]
    public void Escape_on_the_state_driven_path_presses_a_control_that_runs_OnClose()
    {
        // The trap presses the [data-rask-dismiss] control on Escape, so there must be one — and only when there is
        // a callback for it to run.
        Assert.Contains("data-rask-dismiss", UiModal.Title("Delete order").Open(true).OnClose(() => { }).ToHtml());
        Assert.DoesNotContain("data-rask-dismiss", UiModal.Title("Delete order").Open(true).ToHtml());
        Assert.DoesNotContain(
            "data-rask-dismiss",
            UiModal.Title("Delete order").Open(true).OnClose(() => { }).Escapable(false).ToHtml());

        // With no visible close button, a hidden one still takes the Escape.
        var unclosable = UiModal.Title("Delete order").Open(true).OnClose(() => { }).Closable(false).ToHtml();
        Assert.Contains("data-rask-dismiss", unclosable);
        Assert.DoesNotContain("btn-square", unclosable);
    }

    [Fact]
    public void A_side_position_is_a_full_height_flyout_rather_than_a_capped_box()
    {
        // daisyUI's modal-end is already full height; the centred box's max-h and max-w utilities would win
        // over it and float the flyout back into the middle of the edge.
        var flyout = UiModal.Title("Filters").Id("filters").Position(UiModalPosition.End).ToHtml();

        Assert.Contains("modal-end", flyout);
        Assert.DoesNotContain("max-h-[88vh]", flyout);
        Assert.DoesNotContain("sm:max-w-2xl", flyout);
        Assert.Contains("max-h-[88vh]", Popover());
    }

    [Fact]
    public void Closing_is_reported_through_the_dialogs_own_toggle_on_the_modal_path()
    {
        // Handler ids are only written by a live render.
        Assert.Contains(
            "data-rask-on-toggle=",
            global::Rask.Testing.RaskTest.Render(UiModal.Title("Delete order").Id("confirm").OnClose(() => { })).Html);
        Assert.DoesNotContain(
            "data-rask-on-toggle=",
            global::Rask.Testing.RaskTest.Render(UiModal.Title("Delete order").Id("confirm")).Html);
    }

    [Fact]
    public async Task A_dismissal_on_the_state_driven_path_raises_OnCancel_then_OnClose()
    {
        // #1116: Escape and the backdrop are dismissals, so a caller can tell "backed out" from "finished" —
        // cancel first, the order the platform uses on the modal path.
        var heard = new List<string>();
        var page = global::Rask.Testing.RaskTest.Render(UiModal.Title("Edit")
            .Open(true)
            .OnCancel(() => heard.Add("cancel"))
            .OnClose(() => heard.Add("close")));

        await page.On(".modal-backdrop").ClickAsync();
        Assert.Equal(["cancel", "close"], heard);

        heard.Clear();
        await page.On("[data-rask-dismiss]").ClickAsync();
        Assert.Equal(["cancel", "close"], heard);
    }

    [Fact]
    public async Task The_close_button_is_not_a_dismissal()
    {
        var heard = new List<string>();
        var page = global::Rask.Testing.RaskTest.Render(UiModal.Title("Edit")
            .Open(true)
            .OnCancel(() => heard.Add("cancel"))
            .OnClose(() => heard.Add("close")));

        await page.On(".btn-square").ClickAsync();

        Assert.Equal(["close"], heard);
    }

    [Fact]
    public void With_OnCancel_Escape_presses_its_own_control_not_the_close_button()
    {
        // The close button cannot be what Escape presses once the two mean different things.
        var html = UiModal.Title("Edit").Open(true).OnCancel(() => { }).OnClose(() => { }).ToHtml();

        Assert.DoesNotContain("data-rask-dismiss", Tag(html, "<button class=\"btn btn-ghost"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "data-rask-dismiss"));
    }

    [Fact]
    public void OnCancel_alone_still_gives_a_state_driven_dialog_its_ways_out()
    {
        // A page may close the dialog from OnCancel and never set OnClose; the backdrop and Escape must not
        // vanish because only one of the two callbacks is set.
        var html = UiModal.Title("Edit").Open(true).OnCancel(() => { }).ToHtml();

        Assert.Contains("modal-backdrop", html);
        Assert.Contains("data-rask-dismiss", html);
    }

    [Fact]
    public async Task On_the_modal_path_OnCancel_is_the_dialogs_own_cancel_and_the_backdrops_click()
    {
        var cancelled = 0;
        var page = global::Rask.Testing.RaskTest.Render(UiModal.Title("Edit").Id("edit").OnCancel(() => cancelled++));

        Assert.Contains("data-rask-on-cancel=", Tag(page.Html, "<dialog"));

        // The backdrop still closes it in markup; the handler only reports that it was a dismissal.
        Assert.Contains("command=\"close\"", Tag(page.Html, "<button class=\"modal-backdrop"));
        await page.On(".modal-backdrop").ClickAsync();
        Assert.Equal(1, cancelled);

        await page.On("dialog").RaiseAsync("cancel");
        Assert.Equal(2, cancelled);
    }

    [Fact]
    public void Without_OnCancel_the_modal_path_registers_no_handler_for_it()
    {
        var html = global::Rask.Testing.RaskTest.Render(UiModal.Title("Edit").Id("edit")).Html;

        Assert.DoesNotContain("data-rask-on-cancel", html);
        Assert.DoesNotContain("data-rask-on-click", html);
    }

    // The opening tag that starts with `prefix`, so an assertion about one control cannot pass on another's.
    private static string Tag(string html, string prefix)
    {
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no tag starting {prefix}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }

    [Fact]
    public void It_names_itself_through_its_heading() =>
        Assert.Contains("Delete order", Popover());

    [Fact]
    public void The_close_control_in_the_header_is_an_icon_button_that_still_has_a_name()
    {
        var html = Popover();

        Assert.Contains("btn-square", html);
        Assert.Contains("aria-label=\"Close\"", html);
    }

    private static string Popover() =>
        UiModal.Title("Delete order").Id("confirm").Trigger("Delete").ToHtml();
}
