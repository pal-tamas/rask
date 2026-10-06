namespace Rask;

/// <summary>
/// Skeletons that animate together.
/// </summary>
/// <remarks>
/// Flux UI's <c>flux:skeleton.group</c>: a <c>&lt;div&gt;</c> that draws nothing itself. Its
/// <see cref="Animate" /> reaches every <see cref="UiSkeleton" /> and <see cref="UiSkeletonLine" /> inside
/// it, however deep, that does not state its own.
/// </remarks>
public sealed partial class UiSkeletonGroup : Component
{
    /// <summary>How every skeleton inside animates.</summary>
    public Ui.SkeletonAnimate? Animate { get; set; }

    /// <summary>Classes for the group: how it lays its skeletons out.</summary>
    public string? Class { get; set; }

    /// <summary>Inline CSS for the group.</summary>
    public string? Style { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var group = Div.Data("ui-skeleton-group", "").Class(Class).Style(Style);

        return Animate is { } animate
            ? group[Context.Provide(new UiSkeletonScope(animate))[Children ?? []]]
            : group[Children ?? []];
    }
}
