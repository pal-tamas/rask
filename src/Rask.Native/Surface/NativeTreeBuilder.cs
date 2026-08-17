using Rask.Core;
using Rask.Native.Components;

namespace Rask.Native.Surface;

/// <summary>
///     Rebuilds the native view tree from the render walk's balanced enter/exit stream.
/// </summary>
/// <remarks>
///     <para>
///         The walk reports EVERY user component it descends through, not just the native ones. Anything that
///         is not a <see cref="NativeViewComponent" /> is transparent here — which is exactly what lets an app
///         factor a screen into its own components (<c>MyProfileCard</c> rendering a <c>NativeStack</c>) and
///         have the native views underneath still land in the right place.
///     </para>
///     <para>
///         Because the same walk also produces the page HTML, a frame can carry both: the bars go to the chrome
///         descriptor, a <c>NativeWebView</c>'s children to the WebView, and a <see cref="NativeScreen" />'s
///         subtree here.
///     </para>
/// </remarks>
internal sealed class NativeTreeBuilder
{
    // One entry per native component currently open in the walk. The list collects the children built by the
    // exits that happen before this component's own exit.
    private readonly Stack<(NativeViewComponent Component, List<NativeNode> Children)> _open = new();
    private readonly Dictionary<int, Func<string?, Task>> _handlers = [];

    // Handler ids are handed out once per component INSTANCE and never reused, so a node keeps its id across
    // structural churn elsewhere in the tree. Monotonic for the session's lifetime.
    private int _nextHandlerId;

    /// <summary>The screen this frame rendered, or <c>null</c> when it rendered no native content.</summary>
    public NativeNode? Root { get; private set; }

    /// <summary>
    ///     This frame's handler map: id → invoker. Rebuilt every render so an interaction always reaches the
    ///     closure the latest render produced, never a stale one capturing last frame's state.
    /// </summary>
    public IReadOnlyDictionary<int, Func<string?, Task>> Handlers => _handlers;

    /// <summary>Clears the per-frame state. Called before each render walk.</summary>
    public void Reset()
    {
        _open.Clear();
        _handlers.Clear();
        Root = null;
    }

    /// <summary>Opens a component. Non-native components are transparent.</summary>
    public void Enter(Component component)
    {
        if (component is NativeViewComponent view)
        {
            _open.Push((view, []));
        }
    }

    /// <summary>
    ///     Closes a component: materializes its node from the props it writes and the children collected while
    ///     it was open, then hands it to its parent — or keeps it as <see cref="Root" /> when there is none.
    /// </summary>
    public void Exit(Component component)
    {
        if (component is not NativeViewComponent view || _open.Count == 0)
        {
            return;
        }

        var (open, children) = _open.Pop();
        if (!ReferenceEquals(open, view))
        {
            // The stream is produced by a single pre-order walk, so this cannot happen; bailing out beats
            // silently attaching a subtree to the wrong parent if it ever does.
            return;
        }

        AssignHandlerIds(view);
        var props = new NativePropWriter();
        view.WriteSurfaceProps(ref props);

        var node = new NativeNode
        {
            Kind = view.SurfaceKind,
            Key = view.KeyString,
            Props = props.ToArray(),
            Children = children.Count == 0 ? [] : [.. children],
        };

        if (_open.Count > 0)
        {
            _open.Peek().Children.Add(node);
            return;
        }

        // The outermost native component of the frame. The first one wins: a well-formed page has exactly one
        // content root (RASK037 enforces that a screen and a WebView are not siblings).
        Root ??= node;
    }

    // Give the component its ids (once, for its lifetime) and register this frame's invokers under them.
    private void AssignHandlerIds(NativeViewComponent view)
    {
        switch (view)
        {
            case NativeButton { OnClick: var sync, OnClickAsync: var async }
                when sync is not null || async is not null:
                Register(ref view.SurfaceTapId, Tap(sync, async));
                break;

            case NativeStack { OnClick: var sync, OnClickAsync: var async }
                when sync is not null || async is not null:
                Register(ref view.SurfaceTapId, Tap(sync, async));
                break;

            case NativeTextField { OnInput: var sync, OnInputAsync: var async }
                when sync is not null || async is not null:
                Register(ref view.SurfaceChangeId, TextChange(sync, async));
                break;

            case NativeSwitch { OnChanged: var sync, OnChangedAsync: var async }
                when sync is not null || async is not null:
                Register(ref view.SurfaceChangeId, FlagChange(sync, async));
                break;
        }
    }

    private void Register(ref int slot, Func<string?, Task> invoker)
    {
        if (slot < 0)
        {
            slot = _nextHandlerId++;
        }

        _handlers[slot] = invoker;
    }

    // A component may carry both the sync and the async form of a callback; run the synchronous one first and
    // await the other, so neither is silently dropped.
    private static Func<string?, Task> Tap(Action? sync, Func<Task>? async) =>
        _ =>
        {
            sync?.Invoke();
            return async?.Invoke() ?? Task.CompletedTask;
        };

    private static Func<string?, Task> TextChange(Action<string>? sync, Func<string, Task>? async) =>
        value =>
        {
            var text = value ?? string.Empty;
            sync?.Invoke(text);
            return async?.Invoke(text) ?? Task.CompletedTask;
        };

    private static Func<string?, Task> FlagChange(Action<bool>? sync, Func<bool, Task>? async) =>
        value =>
        {
            var on = bool.TryParse(value, out var parsed) && parsed;
            sync?.Invoke(on);
            return async?.Invoke(on) ?? Task.CompletedTask;
        };
}
