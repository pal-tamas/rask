namespace Rask.Ui;

/// <summary>
/// Picks any of the kit's themes, with no JavaScript.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiThemeController" /> is one control for one theme — a light/dark toggle. This is the
/// whole set: a radio group of <c>theme-controller</c> inputs, which daisyUI matches in CSS
/// (<c>input.theme-controller[value=x]:checked</c>), so choosing one rewrites the palette with no
/// script involved. That is why it is radios rather than a <c>&lt;select&gt;</c>: a select's value is
/// only readable from JavaScript, and there is none here.
/// </para>
/// <para>
/// Nothing persists the choice. There is no script, so there is nothing to persist it with, and the
/// theme resets on navigation — the same honest trade <see cref="UiThemeController" /> makes. An app
/// that wants it remembered should render <c>data-theme</c> from its own stored preference instead.
/// </para>
/// <para>
/// It only has an effect inside the kit's theme scope — see
/// <see cref="UiStylesheet.ThemeScopeAttribute" />. Outside it the inputs still render and still check,
/// and nothing changes colour, which is the same silent failure a missing scope causes everywhere else.
/// </para>
/// </remarks>
public sealed partial class UiThemePicker : Component
{
    /// <summary>
    /// The radio group's name, so two pickers on one page do not fight over the same selection.
    /// </summary>
    public string GroupName { get; set; } = "rask-ui-theme";

    /// <summary>
    /// The themes to offer. Defaults to every theme the kit ships.
    /// </summary>
    /// <remarks>
    /// Narrowing this is a presentation choice and does not make the others unavailable: the stylesheet
    /// carries all of them either way, because they are compiled together. Listing a handful here is
    /// for surfaces where thirty-five radio buttons would be the wrong thing to show a reader.
    /// </remarks>
    public IReadOnlyList<UiThemeName>? Themes { get; set; }

    /// <summary>
    /// Whether to offer "follow the operating system" as the first entry. On by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the entry that makes the rest of them reversible. With no saved choice a page following the
    /// reader's machine is the right default, but the moment they try a palette out of curiosity there is
    /// nothing in a list of thirty-five named themes that means "go back to what my computer says" —
    /// and "light" is not that, it is a third answer that happens to match on a light machine.
    /// </para>
    /// <para>
    /// Its value is <see cref="UiTheme.SystemValue" />, which is deliberately not a theme daisyUI
    /// compiled: with it checked no <c>:has(input.theme-controller[value=x]:checked)</c> rule matches, so
    /// the CSS-only half of this control falls back to the default palette on its own. A host that
    /// persists the choice recognises the value and REMOVES its <c>data-theme</c>.
    /// </para>
    /// </remarks>
    public bool ShowSystem { get; set; } = true;

    /// <summary>The label on the "follow the operating system" entry. Defaults to "System".</summary>
    public string SystemLabel { get; set; } = "System";

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose("menu", Class))[
            ShowSystem ? Entry(UiTheme.SystemValue, SystemLabel) : null,
            (Themes ?? UiTheme.All).Select(theme => Entry(UiTheme.Value(theme)))
        ];

    /// <summary>One radio row: the palette's name, or the label the system entry carries.</summary>
    private Component Entry(string value, string? label = null) =>
        // Keyed by the value: the list is stable, but RASK022 holds every list to identity rather than
        // position, and the value is the identity here.
        Li.Key(value)[
            Label.Class("flex cursor-pointer items-center gap-2")[
                // daisyUI keys the palette off the input's `value`, so the chain opens on it. It used to
                // go through the escape hatch, on the belief that Value carried the checked state;
                // Checked does, and Value is the attribute.
                //
                // `theme-controller` on the system row too, even though daisyUI compiled no rule that
                // matches it. The class is what a host's delegated change listener recognises, so
                // leaving it off would make this the one row that reports nothing when it is picked.
                Input
                    .Value(value)
                    .Checked(false)
                    .Type(InputType.Radio)
                    .Name(GroupName)
                    .Class("radio radio-sm theme-controller")
                    .Aria(new Dictionary<string, string?> { ["label"] = label ?? value }),
                Span[label ?? value]
            ]
        ];
}
