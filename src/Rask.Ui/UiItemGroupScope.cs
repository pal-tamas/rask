namespace Rask;

/// <summary>What a <see cref="UiItemGroup" /> tells the items inside it: the surface is the group's.</summary>
internal sealed class UiItemGroupScope
{
    private UiItemGroupScope()
    {
    }

    /// <summary>The one scope every group provides: it carries nothing but its presence.</summary>
    internal static UiItemGroupScope Instance { get; } = new();
}
