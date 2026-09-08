namespace Rask.Ui;

/// <summary>
/// The trail of where this page sits.
/// </summary>
/// <remarks>
/// The last crumb is the current page and is deliberately not a link — a link to where you already are is
/// a dead end that reads as navigation.
/// </remarks>
public sealed partial class UiBreadcrumbs : Component
{
    /// <summary>Each step: the words, and where it goes. A null href is the page you are on.</summary>
    public required IReadOnlyList<(string Text, string? Href)> Items { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose("breadcrumbs text-sm", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = "Breadcrumb" })[
            Ul[
                // Keyed by the crumb's own text: a trail's identity is what it says, and keying by index
                // would let the diff reuse one crumb's element for another when a level is inserted.
                Items.Select(item => Li.Key(item.Text)[
                    item.Href is { } href ? A.Href(href)[item.Text] : Span[item.Text]
                ])
            ]
        ];
}
