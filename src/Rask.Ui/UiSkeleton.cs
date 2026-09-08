namespace Rask.Ui;

/// <summary>
/// The shape of content that has not arrived.
/// </summary>
/// <remarks>
/// <c>aria-hidden</c>, and deliberately: a placeholder has nothing to announce, and a screen reader
/// reading out a row of empty boxes is worse than silence. Pair it with a <see cref="UiLoading" /> where
/// the wait itself needs announcing.
/// </remarks>
public sealed partial class UiSkeleton : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("skeleton", Class)).Attributes(("aria-hidden", "true"))[Children ?? []];
}
