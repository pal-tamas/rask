namespace Rask.Ui;

/// <summary>
/// A badge pinned to the corner of something.
/// </summary>
/// <remarks>
/// The badge goes in <see cref="Badge" /> and the thing it marks is the children. Two slots rather than
/// one, because daisyUI needs the badge to carry <c>indicator-item</c> and the caller should not have to
/// remember which of two children gets it.
/// </remarks>
public sealed partial class UiIndicator : Component
{
    public required Component Badge { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("indicator", Class))[
            Span.Class("indicator-item")[Badge],
            Children ?? []
        ];
}
