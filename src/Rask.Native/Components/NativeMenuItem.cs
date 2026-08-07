namespace Rask.Native.Components;

/// <summary>
///     A single entry in a <see cref="NativeMenuButton" />'s menu — a title, an optional icon, and an action.
///     Projected to a <c>UIAction</c> in an iOS <c>UIMenu</c> / an Android <c>PopupMenu</c> item; selecting it
///     runs <see cref="OnClick" /> on the render thread and re-renders, like any Rask callback.
/// </summary>
public sealed class NativeMenuItem : NativeBarItem
{
    /// <summary>The menu entry's label. Required.</summary>
    public new required string Title { get; set; }

    /// <summary>An optional leading icon for the entry.</summary>
    public NativeIcon? Icon { get; set; }

    /// <summary>Invoked when the entry is selected. Optional (an entry may be display-only).</summary>
    public Action? OnClick { get; set; }

    /// <summary>
    ///     When <c>true</c>, the entry is styled as destructive (iOS renders it in red). Nullable so it stays an
    ///     optional factory parameter; <c>null</c> is treated as <c>false</c>.
    /// </summary>
    public bool? Destructive { get; set; }
}
