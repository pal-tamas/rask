namespace Rask.Ui;

/// <summary>
/// A control that selects a theme.
/// </summary>
/// <remarks>
/// <para>
/// It reports a choice and draws itself as chosen; it does not apply the theme, because it cannot. The
/// palette is set by <c>data-theme</c> on the element carrying
/// <see cref="UiStylesheet.ThemeScopeAttribute" />, which is an ANCESTOR — usually <c>&lt;html&gt;</c>,
/// from the root component's <c>Shell</c> override — and no component can write an attribute onto
/// something above it. The page holds the chosen theme and puts <c>UiTheme.Value(theme)</c> there.
/// </para>
/// <para>
/// This is what replaced daisyUI's <c>theme-controller</c> input, which flipped the palette from CSS
/// alone by matching <c>input.theme-controller[value=x]:checked</c>. That was genuinely free — no script,
/// no state — and it is worth being clear about what the trade bought: because nothing in C# knew which
/// theme was showing, the choice could not be persisted, could not be read back, and reset itself on
/// every navigation. A page that owns the value can store it.
/// </para>
/// </remarks>
public sealed partial class UiThemeController : Component
{
    /// <summary>
    ///     What this control is called — for example "Dark". Free to use here because this component
    ///     renders no <c>&lt;label&gt;</c> element of its own.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>The theme this control selects.</summary>
    public required UiThemeName Theme { get; set; }

    /// <summary>Whether this is the theme currently showing.</summary>
    public bool? Active { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>Runs when it is chosen, with the theme the reader asked for.</summary>
    public Callback<UiThemeName>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var button = Button
            .Type("button")
            .Class(UiClass.Compose(
                "btn",
                Size is { } size ? UiClassNames.ButtonSize(size) : "",
                Active == true ? "btn-active" : "",
                Class))
            .Aria(new Dictionary<string, string?> { ["pressed"] = Active == true ? "true" : "false" });

        if (OnChange is { } change)
        {
            button = button.OnClick(() => change.Invoke(Theme) ?? Task.CompletedTask);
        }

        return button[Span[Label]];
    }
}
