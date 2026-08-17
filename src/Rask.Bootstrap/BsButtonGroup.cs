namespace Rask.Bootstrap;

// A Bootstrap button group: <div class="btn-group" role="group">. Holds BsButton children. Set
// Vertical for a stacked group, and Size for btn-group-sm / btn-group-lg.

/// <summary>
///     A set of related buttons joined into one control. Give it an <c>Aria</c> label — the grouping is
///     visual, and a screen reader otherwise hears only loose buttons.
/// </summary>
public sealed partial class BsButtonGroup : BsBlock
{
    /// <summary>Stacks the buttons vertically.</summary>
    public bool? Vertical { get; set; }

    /// <summary>Makes every button in the group smaller or larger.</summary>
    public BsSize? Size { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join(
            Vertical is true ? "btn-group-vertical" : "btn-group",
            Size is { } s && s.Suffix() is { } suffix ? $"btn-group-{suffix}" : null,
            Class))
        .Role("group")[Items];
}
