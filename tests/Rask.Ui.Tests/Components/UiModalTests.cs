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
    [InlineData(UiModalPlacement.Top, "modal-top")]
    [InlineData(UiModalPlacement.Middle, "modal-middle")]
    [InlineData(UiModalPlacement.Bottom, "modal-bottom")]
    [InlineData(UiModalPlacement.Start, "modal-start")]
    [InlineData(UiModalPlacement.End, "modal-end")]
    public void Every_placement_writes_its_own_class(UiModalPlacement placement, string expected) =>
        Assert.Contains(expected,
            UiModal.Title("Delete order").Id("confirm").Placement(placement).ToHtml());

    [Fact]
    public void A_stated_placement_replaces_the_responsive_default_rather_than_fighting_it()
    {
        // Appending the default would leave `sm:modal-middle` overriding the caller's choice at every
        // width above a phone — the class present, and ignored.
        var html = UiModal.Title("Delete order").Id("confirm").Placement(UiModalPlacement.Top).ToHtml();

        Assert.DoesNotContain("sm:modal-middle", html);
        Assert.DoesNotContain("modal-bottom", html);
    }

    [Fact]
    public void The_backdrop_button_belongs_only_to_the_state_driven_path()
    {
        // On the popover path the browser light-dismisses, so a button to do it would be a second,
        // worse implementation of something already there.
        Assert.DoesNotContain("modal-backdrop", Popover());
        Assert.Contains("modal-backdrop",
            UiModal.Title("Delete order").Open(true).Close(() => { }).ToHtml());
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
