namespace Rask.Ui.Tests.Components;

/// <summary>
///     The theme control, which reports a choice rather than applying one.
/// </summary>
public partial class UiThemeControllerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_renders_a_button_and_no_theme_controller_input()
    {
        // daisyUI's `input.theme-controller[value=x]:checked` flipped the palette from CSS alone, which
        // was free — and left nothing in C# knowing which theme was showing, so the choice could not be
        // persisted or read back and reset itself on every navigation.
        var html = Control(active: null);

        Assert.Contains("<button", html);
        Assert.DoesNotContain("theme-controller", html);
        Assert.DoesNotContain("<input", html);
    }

    [Fact]
    public void It_writes_no_data_theme_of_its_own()
    {
        // It cannot: the palette is set on the element carrying the theme scope, which is an ANCESTOR,
        // and no component can write an attribute onto something above it. The page does that.
        Assert.DoesNotContain("data-theme", Control(active: true));
    }

    [Fact]
    public void The_chosen_theme_is_drawn_as_pressed() =>
        Assert.Contains("btn-active", Control(active: true));

    [Fact]
    public void An_unchosen_theme_is_not() =>
        Assert.DoesNotContain("btn-active", Control(active: false));

    [Fact]
    public void It_announces_whether_it_is_the_current_theme()
    {
        Assert.Contains("aria-pressed=\"true\"", Control(active: true));
        Assert.Contains("aria-pressed=\"false\"", Control(active: false));
    }

    [Fact]
    public void It_shows_its_label() =>
        Assert.Contains("<span>Dark</span>", Control(active: null));

    [Theory]
    [InlineData(UiSize.Xs, "btn-xs")]
    [InlineData(UiSize.Lg, "btn-lg")]
    public void It_takes_a_size(UiSize size, string expected) =>
        Assert.Contains(expected,
            UiThemeController.Label("Dark").Theme(UiThemeName.Dark).Size(size).ToHtml());

    [Fact]
    public void The_theme_is_stated_as_a_name_rather_than_a_string()
    {
        // It used to be `string? Theme`, defaulting to "dark". A misspelled theme is not a compile error
        // and produces no visible failure — daisyUI simply matches nothing — so the closed set is what
        // makes the mistake unrepresentable.
        foreach (var theme in Enum.GetValues<UiThemeName>())
        {
            Assert.Contains("btn", UiThemeController.Label("x").Theme(theme).ToHtml());
        }
    }

    private string Control(bool? active) =>
        UiThemeController.Label("Dark").Theme(UiThemeName.Dark).Active(active).ToHtml();
}
