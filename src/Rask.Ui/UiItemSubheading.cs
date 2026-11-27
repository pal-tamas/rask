namespace Rask;

/// <summary>
///     Supporting text, tucked under an item's heading. Flux UI's <c>item.subheading</c>.
/// </summary>
/// <remarks>A <c>&lt;p&gt;</c> in 13px zinc-500 (white at 70% in dark), 2px under the heading.</remarks>
public sealed partial class UiItemSubheading : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-item-subheading");

    /// <inheritdoc />
    protected override string TagName => "p";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "col-start-2 row-start-3 [:where(&)]:min-w-0 wrap-break-word [:where(&)]:mt-0.5 [:where(&)]:text-[13px]/[18px] [:where(&)]:text-zinc-500 dark:text-white/70",
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
