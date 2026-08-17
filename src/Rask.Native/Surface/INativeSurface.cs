namespace Rask.Native.Surface;

/// <summary>What happened to a node the user touched.</summary>
public enum NativeSurfaceEventKind
{
    /// <summary>A button (or another tappable node) was tapped.</summary>
    Tap,

    /// <summary>A value-bearing control changed — a text field's text, a switch's state.</summary>
    Change,
}

/// <summary>
///     A user interaction on a native view, echoed back by the surface backend.
/// </summary>
/// <param name="HandlerId">
///     The id the node carried as <c>TapId</c>/<c>ChangeId</c>. The session resolves it against the handler map
///     it rebuilds every render, so the delegate invoked is always the current closure.
/// </param>
/// <param name="Kind">Whether this was a tap or a value change.</param>
/// <param name="Value">
///     The control's new value for <see cref="NativeSurfaceEventKind.Change" /> — a text field's text, or
///     <c>"true"</c>/<c>"false"</c> for a switch. <c>null</c> for a tap.
/// </param>
public readonly record struct NativeSurfaceEvent(int HandlerId, NativeSurfaceEventKind Kind, string? Value);

/// <summary>
///     The backend that turns a <see cref="NativeNode" /> tree into real platform views — a <c>UIView</c> tree
///     on iOS, an <c>android.view.View</c> tree on Android — so a Rask page renders with no WebView at all.
///     Register an implementation on <c>host.Services</c> before <c>RunLocalAsync</c>, exactly like
///     <see cref="INativeChrome" />; with none registered the pure-native components are inert and an app keeps
///     rendering through the WebView as before.
/// </summary>
/// <remarks>
///     A single app mixes both: each frame is either an HTML frame (the page composed a <c>NativeWebView</c>)
///     or a native frame (it composed a <c>NativeScreen</c>), and the session calls
///     <see cref="ShowWebViewAsync" /> or <see cref="MountAsync" />/<see cref="PatchAsync" /> accordingly. So
///     one tab of a <c>NativeTabBar</c> can be a web page and the next a pure-native screen.
///     <para>
///         <b>An implementation must never destroy either content view when switching</b> — hide the one that
///         is not showing and keep it, with its subviews, alive. Both of the session's diff baselines (the
///         HTML byte buffer and the retained node tree) describe views that are merely hidden, so tearing one
///         down would leave the session patching against a view that no longer exists.
///     </para>
///     <para>
///         All three methods are called on the render thread. A UIKit/Android implementation marshals to the
///         UI thread itself, exactly as <see cref="INativeChrome.ApplyChromeAsync" /> does.
///     </para>
/// </remarks>
public interface INativeSurface
{
    /// <summary>
    ///     This frame's content is HTML: show the WebView and hide the native content view, keeping the latter
    ///     and its retained tree intact for when a native route comes back.
    /// </summary>
    ValueTask ShowWebViewAsync();

    /// <summary>
    ///     Build <paramref name="root" />'s whole subtree from scratch, make it the native content view, and
    ///     show it. Called for the first native frame and whenever the retained tree cannot be patched into the
    ///     new one (a different root kind).
    /// </summary>
    ValueTask MountAsync(NativeNode root);

    /// <summary>
    ///     Apply <paramref name="patches" />, in order, to the already-mounted tree, then show the native
    ///     content view. An empty list still means "this frame is native" — it is the signal to switch back
    ///     from the WebView when the content happens to be unchanged.
    /// </summary>
    ValueTask PatchAsync(IReadOnlyList<NativePatch> patches);

    /// <summary>
    ///     Set by the host to receive interactions. The backend invokes it with the node's handler id; the
    ///     session looks the id up, runs the delegate, re-renders, and pushes the resulting patches back.
    /// </summary>
    Func<NativeSurfaceEvent, Task>? OnSurfaceEvent { get; set; }
}
