namespace Rask.Native.Components;

/// <summary>
///     An item hosted inside a native bar — a button (<see cref="NativeBarButton" />), a back button
///     (<see cref="NativeBackButton" />), or a tab (<see cref="NativeTab" />). Used as the element type of a
///     bar's item slots (<c>Leading</c>/<c>Trailing</c>/<c>Items</c>/<c>Tabs</c>); not composed in the render
///     tree on its own.
/// </summary>
public abstract partial class NativeBarItem : NativeComponent
{
    private protected NativeBarItem() { }
}
