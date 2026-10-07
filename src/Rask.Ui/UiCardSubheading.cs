namespace Rask;

/// <summary>
/// Supporting text, tucked under the <see cref="UiCardHeading" /> before it. Flux UI's <c>card.subheading</c>.
/// </summary>
public sealed partial class UiCardSubheading : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        P.Class(UiClass.Compose("mt-1 text-sm text-zinc-500 dark:text-white/70", Class))
            .Data(("ui-card-subheading", null), ("ui-text", null))[Children ?? []];
}
