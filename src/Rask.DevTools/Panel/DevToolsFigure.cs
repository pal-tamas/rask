using Rask.Core;

namespace Rask.DevTools.Panel;

/// <summary>One figure at the head of a tab: what it counts, and the number.</summary>
/// <remarks>Flux's dashboard figure in a small card: Flux's text over the value as a Flux heading.</remarks>
internal sealed partial class DevToolsFigure : Component
{
    /// <summary>What the figure counts.</summary>
    public required string Label { get; set; }

    /// <summary>The number, formatted.</summary>
    public string? Value { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Card.Size(Ui.CardSize.Sm)[
            Ui.Text.Size(Ui.TextSize.Sm).Class("truncate")[Label],
            Ui.Heading.Lg.Class("mt-1 tabular-nums")[Value]
        ];
}
