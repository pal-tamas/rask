namespace Rask.Ui;

/// <summary>
/// A browser window around a picture of a page.
/// </summary>
public sealed partial class UiMockupBrowser : Component
{
    /// <summary>The address shown in the bar.</summary>
    public string? Url { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-browser border border-base-300 bg-base-100", Class))[
            Div.Class("mockup-browser-toolbar")[
                Url is { } url ? Div.Class("input")[url] : null
            ],
            Div.Class("border-t border-base-300")[Children ?? []]
        ];
}
