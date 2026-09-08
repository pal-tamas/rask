namespace Rask.Ui;

/// <summary>
/// One row of a <see cref="UiList" />.
/// </summary>
/// <remarks>
/// The child marked <c>list-col-grow</c> is the one that takes the remaining width; daisyUI gives every
/// other child its intrinsic size. Set <see cref="Grow" /> on the part that should stretch, which is
/// almost always the text rather than the picture beside it.
/// </remarks>
public sealed partial class UiListRow : Component
{
    /// <summary>The part that takes the remaining width.</summary>
    public required Component Grow { get; set; }

    /// <summary>Before the growing part — a picture, an icon, an index.</summary>
    public Component? Leading { get; set; }

    /// <summary>After it — usually the actions.</summary>
    public Component? Trailing { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Li.Class(UiClass.Compose("list-row", Class))[
            Leading,
            Div.Class("list-col-grow")[Grow],
            Trailing
        ];
}
