namespace Rask.Native.Components;

/// <summary>
///     A native overflow / pull-down menu button for a bar (header <c>Leading</c>/<c>Trailing</c> or a
///     <see cref="NativeToolbar" />'s items). Tapping it opens a native menu of <see cref="NativeMenuItem" />s —
///     an iOS <c>UIMenu</c> pull-down on a <c>UIBarButtonItem</c>, an Android <c>PopupMenu</c> — for secondary
///     actions that don't warrant their own bar button.
/// </summary>
/// <example>
///     <code>NativeHeaderBar(Title: "Home", Trailing:
///     [
///         NativeMenuButton(Items:
///         [
///             NativeMenuItem(Title: "Refresh", Icon: NativeIcon.Search, OnClick: OnRefresh),
///             NativeMenuItem(Title: "Delete", Destructive: true, OnClick: OnDelete),
///         ]),
///     ])</code>
/// </example>
public sealed partial class NativeMenuButton : NativeBarItem
{
    /// <summary>The button's icon. Optional — defaults to a platform "more" (ellipsis) glyph when null.</summary>
    public NativeIcon? Icon { get; set; }

    /// <summary>The menu entries, in order. Nullable with no initializer so it stays a factory parameter.</summary>
    public IReadOnlyList<NativeMenuItem>? Items { get; set; }

    /// <summary>An optional accessibility label for the button (defaults to "More").</summary>
    public new string? Title { get; set; }
}
