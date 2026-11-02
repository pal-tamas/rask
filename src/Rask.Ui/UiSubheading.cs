namespace Rask;

/// <summary>
/// A line of supporting text under a <see cref="UiHeading" />, which keeps 8px from it.
/// </summary>
/// <remarks>
/// Flux UI's <c>flux:subheading</c>, as its modal page draws one: a <c>&lt;div&gt;</c> in 14px zinc-500
/// (white at 70% in dark). The heading before or after it takes the 8px between them itself.
/// </remarks>
public sealed partial class UiSubheading : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-subheading");

    /// <inheritdoc />
    protected override string TagName => "div";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose("text-sm [:where(&)]:text-zinc-500 [:where(&)]:dark:text-white/70", Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);
}
