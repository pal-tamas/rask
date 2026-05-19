using System.Text;
using System.Text.Encodings.Web;
using Rask.Core.Components;
using Rask.Core.Live;


namespace Rask.Core;

internal static class HtmlSerializer
{
    private static readonly HashSet<string> _shellTags = new(StringComparer.Ordinal)
    {
        "html",
        "head",
        "body",
        "title",
        "meta",
        "link",
        "script",
        "style",
        "base"
    };

    public static void Serialize(Component component, StringBuilder sb)
    {
        switch (component)
        {
            case Text t:
                sb.Append(HtmlEncoder.Default.Encode(t.Value ?? string.Empty));
                break;

            case Raw r:
                sb.Append(r.Value);
                break;

            case Doctype:
                sb.Append("<!DOCTYPE html>");
                break;

            case Fragment fragment:
                if (fragment.Children is { } fragmentChildren)
                {
                    foreach (var child in fragmentChildren)
                    {
                        Serialize(child.Component, sb);
                    }
                }

                break;

            case ErrorBoundary boundary:
                SerializeErrorBoundary(boundary, sb);
                break;

            case { TagNameInternal: { } tagName } el:
                sb.Append('<').Append(tagName);
                el.WriteAttributesInternal(sb);

                var live = LiveRenderContext.Current;
                var scopeId = live?.CurrentScopeId;
                var isShell = _shellTags.Contains(tagName);
                if (scopeId is not null && !isShell)
                {
                    sb.Append(" data-").Append(scopeId);
                }

                if (live?.PendingMountScopeId is { } mountScope && !isShell)
                {
                    sb.Append(" data-rask-mount=\"").Append(mountScope).Append('"');
                    live.PendingMountScopeId = null;
                }

                if (el.SelfClosingInternal)
                {
                    sb.Append(" />");
                    break;
                }

                sb.Append('>');
                using (el.EnterChildrenScopeInternal())
                {
                    foreach (var child in el.RenderChildrenInternal())
                    {
                        Serialize(child.Component, sb);
                    }
                }

                sb.Append("</").Append(tagName).Append('>');
                break;

            default:
                // Push the parent scope for the entire duration of serialising this user
                // component — including the walk of its rendered subtree. That way
                // factories called from inside its Render AND handlers registered on
                // elements deep in its rendered tree both attribute back to this component.
                var liveCtx = LiveRenderContext.Current;
                if (liveCtx is not null && component.Boundary is null)
                {
                    // Stamp the nearest enclosing boundary on first traversal so async
                    // lifecycle and event-handler catch sites can find it later.
                    component.Boundary = liveCtx.CurrentBoundary;
                }

                // Collect this component's Head contribution before entering its render
                // scope. The registry is consumed once at the end of RenderAsLiveRoot when
                // it replaces the RaskHeadAssets sentinel — components that go away on the
                // next render just stop contributing and their head tags fall out naturally.
                if (liveCtx is not null && component.HeadInternal is { } head)
                {
                    liveCtx.HeadAssets.Add(head);
                }

                using (liveCtx?.PushScope(component))
                using (liveCtx?.EnterParentScope(component))
                using (component.EnterChildrenScopeInternal())
                {
                    Serialize(component.RenderForLive(), sb);
                }

                break;
        }
    }

    private static void SerializeErrorBoundary(ErrorBoundary boundary, StringBuilder sb)
    {
        var live = LiveRenderContext.Current;
        using (live?.PushScope(boundary))
        using (live?.EnterParentScope(boundary))
        using (live?.PushBoundary(boundary))
        {
            var saved = sb.Length;
            try
            {
                Serialize(boundary.RenderForLive(), sb);
            }
            catch (Exception ex) when (boundary.Error is null)
            {
                // Rewind anything the failing subtree wrote so the fallback replaces it
                // cleanly. The guard prevents recursive catching of a fallback that itself
                // throws — that escape bubbles to the next outer boundary instead.
                sb.Length = saved;
                boundary.Trip(ex);
                Serialize(boundary.RenderForLive(), sb);
            }
        }
    }
}
