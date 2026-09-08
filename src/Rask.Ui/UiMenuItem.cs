namespace Rask.Ui;

/// <summary>
/// One entry in a <see cref="UiMenu" />.
/// </summary>
public sealed partial class UiMenuItem : Component
{
    public new required string Text { get; set; }

    public string? Href { get; set; }

    public UiIconName? Icon { get; set; }

    /// <summary>The page this entry leads to is the page being shown.</summary>
    public bool? Active { get; set; }

    public Action? OnClick { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component inner = Href is { } href
            ? A.Href(href).Class(Active == true ? "menu-active" : "")[Content()]
            : Button.Type("button").Class(Active == true ? "menu-active" : "").OnClick(OnClick)[Content()];

        return Li.Class(UiClass.Compose(Class))[inner];
    }

    private Component Content() =>
        [
            Icon is { } icon ? UiIcon.Name(icon).Class("size-4 shrink-0") : null,
            Span[Text]
        ];
}
