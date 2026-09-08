namespace Rask.Ui.Tests.Components;

/// <summary>
///     The swap, which draws its state from a class rather than from a checkbox.
/// </summary>
public partial class UiSwapTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_renders_a_button_and_no_input()
    {
        // The checkbox version kept the state in the DOM, where C# could neither read it nor correct it.
        // With the input gone the control has to be a button: a <label> is only focusable because of the
        // input inside it, so a label with none would have been an unreachable control.
        var html = Swap(active: null);

        Assert.Contains("<button", html);
        Assert.DoesNotContain("<input", html);
    }

    [Fact]
    public void Active_shows_the_on_face() =>
        Assert.Contains("swap-active", Swap(active: true));

    [Fact]
    public void Inactive_writes_no_class_and_shows_the_off_face() =>
        Assert.DoesNotContain("swap-active", Swap(active: false));

    [Fact]
    public void Both_faces_are_always_rendered()
    {
        // daisyUI cross-fades between them, so both have to be present; showing one is CSS's job.
        var html = Swap(active: true);

        Assert.Contains("swap-on", html);
        Assert.Contains("swap-off", html);
    }

    [Theory]
    [InlineData(UiSwapAnimation.Rotate, "swap-rotate")]
    [InlineData(UiSwapAnimation.Flip, "swap-flip")]
    public void Every_animation_writes_its_own_class(UiSwapAnimation animation, string expected) =>
        Assert.Contains(expected,
            UiSwap.AccessibleLabel("Mute").On(Span["on"]).Off(Span["off"]).Animation(animation).ToHtml());

    [Fact]
    public void The_default_animation_writes_no_class() =>
        Assert.DoesNotContain("swap-rotate",
            UiSwap.AccessibleLabel("Mute").On(Span["on"]).Off(Span["off"])
                .Animation(UiSwapAnimation.Default).ToHtml());

    [Fact]
    public void It_carries_the_accessible_name_the_faces_cannot_give_it() =>
        Assert.Contains("aria-label=\"Mute\"", Swap(active: null));

    [Fact]
    public void It_announces_which_face_is_showing()
    {
        // A toggle that looks pressed and does not say so is a toggle a screen reader reads as an
        // ordinary button, with the state carried only by an icon it cannot see.
        Assert.Contains("aria-pressed=\"true\"", Swap(active: true));
        Assert.Contains("aria-pressed=\"false\"", Swap(active: false));
    }

    private string Swap(bool? active) =>
        UiSwap.AccessibleLabel("Mute").On(Span["on"]).Off(Span["off"]).Active(active).ToHtml();
}
