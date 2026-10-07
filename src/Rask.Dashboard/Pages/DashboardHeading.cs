namespace Rask.Dashboard.Pages;

/// <summary>A console page's heading: what it is, a line about its state, and what can be done to it.</summary>
/// <remarks>
/// Composed from the kit, because the console writes no classes: a heading, a subheading and the actions,
/// one under the other. The kit's own page header became Flux's layout header (<c>Ui.Header</c>).
/// </remarks>
internal sealed partial class DashboardHeading : Component
{
    public new required string Title { get; set; }

    public string? Caption { get; set; }

    public Component? Actions { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div[
            Ui.Heading.Level(1).Size(Ui.Size.Xl)[Title],
            Caption is null ? null : Ui.Subheading[Caption],
            Actions is null ? null : Div[Actions]
        ];
}
