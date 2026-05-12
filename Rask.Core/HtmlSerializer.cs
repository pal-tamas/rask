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
                sb.Append(HtmlEncoder.Default.Encode(t.Value));
                break;

            case Raw r:
                sb.Append(r.Value);
                break;

            case Doctype:
                sb.Append("<!DOCTYPE html>");
                break;

            case Fragment fragment:
                foreach (var child in fragment.Children)
                {
                    Serialize(child.Component, sb);
                }

                break;

            case ErrorBoundary boundary:
                SerializeErrorBoundary(boundary, sb);
                break;

            case IElement el:
                sb.Append('<').Append(el.TagNameInternal);
                foreach (var (name, value) in el.AttributesInternal())
                {
                    sb.Append(' ').Append(name);
                    if (value is not null)
                    {
                        sb.Append("=\"").Append(HtmlEncoder.Default.Encode(value)).Append('"');
                    }
                }

                var scopeId = LiveRenderContext.Current?.CurrentScopeId;
                if (scopeId is not null && !_shellTags.Contains(el.TagNameInternal))
                {
                    sb.Append(" data-").Append(scopeId);
                }

                if (el.SelfClosingInternal)
                {
                    sb.Append(" />");
                    break;
                }

                sb.Append('>');
                using (el.EnterChildrenScope())
                {
                    foreach (var child in el.ChildrenInternal)
                    {
                        Serialize(child.Component, sb);
                    }
                }

                sb.Append("</").Append(el.TagNameInternal).Append('>');
                break;

            default:
                // Push the parent scope for the entire duration of serialising this user
                // component — including the walk of its rendered subtree. That way
                // factories called from inside its Render AND handlers registered on
                // elements deep in its rendered tree both attribute back to this component.
                var live = LiveRenderContext.Current;
                if (live is not null && component.Boundary is null)
                {
                    // Stamp the nearest enclosing boundary on first traversal so async
                    // lifecycle and event-handler catch sites can find it later.
                    component.Boundary = live.CurrentBoundary;
                }

                using (live?.PushScope(component))
                using (live?.EnterParentScope(component))
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
