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

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ul.Class(UiClass.Compose("menu", Class))[
            (Themes ?? UiTheme.All).Select(theme =>
            {
                var value = UiTheme.Value(theme);

                // Keyed by the theme's own name: the list is stable, but RASK022 holds every list to
                // identity rather than position, and the name is the identity here.
                return Li.Key(value)[
                    Label.Class("flex cursor-pointer items-center gap-2")[
                        Input
                            .Value(false)
                            .Type(InputType.Radio)
                            .Name(GroupName)
                            .Class("radio radio-sm theme-controller")
                            // daisyUI keys the palette off the input's `value`, which is a plain
                            // attribute here rather than the chain's Value — that one carries the
                            // checked state.
                            .Attributes(("value", value))
                            .Aria(new Dictionary<string, string?> { ["label"] = value }),
                        Span[value]
                    ]
                ];
            })
        ];
}
