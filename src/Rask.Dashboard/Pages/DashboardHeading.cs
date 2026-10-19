namespace Rask.Dashboard.Pages;

/// <summary>A console page's heading: what it is, a line about its state, and what can be done to it.</summary>
/// <remarks>
/// Composed from Flux's pieces: a heading with a line of text under it, and the actions beside them — below
/// them on a phone, where the two would not fit side by side.
/// </remarks>
internal sealed partial class DashboardHeading : Component
{
    public new required string Title { get; set; }

    public string? Caption { get; set; }

    public Component? Actions { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-end justify-between gap-4")[
            Div.Class("min-w-0")[
                Ui.Heading.Level(1).Xl[Title],
                Caption is null ? null : Ui.Text.Class("mt-1")[Caption]
            ],
            Actions is null ? null : Div.Class("flex flex-wrap items-center gap-2")[Actions]
        ];
}
