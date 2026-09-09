namespace Rask.Ui;

/// <summary>
/// The shape of content that has not arrived.
/// </summary>
/// <remarks>
/// <c>aria-hidden</c>, and deliberately: a placeholder has nothing to announce, and a screen reader
/// reading out a row of empty boxes is worse than silence. Pair it with a <see cref="UiLoading" /> where
/// the wait itself needs announcing.
/// </remarks>
public sealed partial class UiSkeleton : Div
{
    // aria-hidden, because a skeleton is a placeholder for content that has not arrived: announcing it
    // tells a screen-reader user about a box that means nothing.
    /// <inheritdoc />
    protected override void WriteAttributes(System.Text.StringBuilder sb)
    {
        base.WriteAttributes(sb);
        AppendAttr(sb, "aria-hidden", "true");
    }

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose("skeleton", Class);
}
