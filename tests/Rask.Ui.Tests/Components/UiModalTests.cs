namespace Rask.Ui.Tests.Components;

/// <summary>
///     The dialog, now on one path instead of two.
/// </summary>
public partial class UiModalTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rendering_it_at_all_is_asking_for_it_to_show()
    {
        // Unset means open. A dialog that rendered nothing visible by default would be a trap: the
        // ordinary way to ask for one is to render it when your state says so.
        Assert.Contains("modal-open", UiModal.Title("Delete order").ToHtml());
    }

    [Fact]
    public void It_can_be_kept_mounted_but_hidden() =>
        Assert.DoesNotContain("modal-open", UiModal.Title("Delete order").Open(false).ToHtml());

    [Fact]
    public void Without_a_stated_placement_it_is_a_sheet_on_a_phone_and_centred_above_it()
    {
        // Not a stylistic default: a centred dialog on a 360px screen either overflows or shrinks its
        // content past readable, and a stack trace is the one thing here that must stay readable.
        var html = UiModal.Title("Delete order").ToHtml();

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
        Assert.Contains(expected, UiModal.Title("Delete order").Placement(placement).ToHtml());

    [Fact]
    public void A_stated_placement_replaces_the_responsive_default_rather_than_fighting_it()
    {
        // Appending the default would leave `sm:modal-middle` overriding the caller's choice at every
        // width above a phone — the class would be present, and ignored.
        var html = UiModal.Title("Delete order").Placement(UiModalPlacement.Top).ToHtml();

        Assert.DoesNotContain("sm:modal-middle", html);
        Assert.DoesNotContain("modal-bottom", html);
    }

    [Fact]
    public void It_is_a_dialog_to_assistive_technology_and_names_itself()
    {
        var html = UiModal.Title("Delete order").ToHtml();

        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        Assert.Contains("aria-label=\"Delete order\"", html);
    }

    [Fact]
    public void The_backdrop_only_exists_when_there_is_something_to_close_it_with()
    {
        // It is a pointer convenience. With no Close there is nothing for a click outside to do, and an
        // element that swallows clicks and does nothing is worse than no element.
        Assert.DoesNotContain("modal-backdrop", UiModal.Title("Delete order").ToHtml());
        Assert.Contains("modal-backdrop", UiModal.Title("Delete order").Close(() => { }).ToHtml());
    }

    [Fact]
    public void The_footer_only_exists_when_given_one() =>
        Assert.DoesNotContain("border-t", UiModal.Title("Delete order").ToHtml());

    [Fact]
    public void The_close_control_in_the_header_is_an_icon_button_that_still_has_a_name()
    {
        // The keyboard path out. It is square, so its label cannot be visible text.
        var html = UiModal.Title("Delete order").ToHtml();

        Assert.Contains("btn-square", html);
        Assert.Contains("aria-label=\"Close\"", html);
    }
}
