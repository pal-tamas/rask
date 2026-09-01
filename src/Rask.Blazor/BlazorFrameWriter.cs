using System.Text;
using Microsoft.AspNetCore.Components.RenderTree;
using Rask.Core;

namespace Rask.Blazor;

/// <summary>
///     Writes a hosted component's render tree to HTML, keeping its event handlers alive.
/// </summary>
/// <remarks>
///     <para>
///         This exists instead of <c>StaticHtmlRenderer.WriteComponentHtml</c> for one reason: that
///         method DROPS event handlers. Blazor assigns a real handler id to every <c>@onclick</c> even
///         in a static render — the frames carry <c>AttributeEventHandlerId</c> 1, 2, … — and the
///         built-in writer simply does not emit them, because in Blazor's own model the circuit is
///         what wires events up.
///     </para>
///     <para>
///         Rask already has a channel for exactly this. So each Blazor handler id is registered as an
///         ordinary Rask handler and written as <c>data-rask-on-{event}</c>; the browser's existing
///         delegated listener sends it over the socket that is already open, and the island dispatches
///         it back into Blazor. No circuit, no SignalR, no <c>blazor.web.js</c>, no second connection —
///         the same contract the React and Lit islands use for their callbacks.
///     </para>
///     <para>
///         The dependency this buys is on <c>RenderTreeFrame</c>'s SHAPE, which is what BL0006 warns
///         about. It is read-only: we never construct or mutate a frame. A change to those types
///         breaks this file's compile rather than its behaviour, which is the failure mode to prefer.
///     </para>
/// </remarks>
internal static class BlazorFrameWriter
{
    // Elements with no closing tag. Anything not here gets <tag>…</tag>, which is correct even for an
    // empty element.
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Renders <paramref name="componentId" />'s current tree.</summary>
    /// <param name="renderer">The island's renderer, used to reach nested components' frames.</param>
    /// <param name="componentId">The root to write.</param>
    /// <param name="registerEvent">
    ///     Turns a Blazor handler id into the Rask handler id to write. Returning null omits the
    ///     attribute, which is what happens when there is no live session to dispatch through.
    /// </param>
    public static string Write(
        BlazorIslandRenderer renderer,
        int componentId,
        Func<ulong, string, string?> registerEvent)
    {
        var sb = new StringBuilder();
        WriteComponent(renderer, componentId, sb, registerEvent);
        return sb.ToString();
    }

    private static void WriteComponent(
        BlazorIslandRenderer renderer,
        int componentId,
        StringBuilder sb,
        Func<ulong, string, string?> registerEvent)
    {
        var frames = renderer.FramesFor(componentId);
        WriteRange(renderer, frames, 0, frames.Count, sb, registerEvent);
    }

    private static void WriteRange(
        BlazorIslandRenderer renderer,
        ArrayRange<RenderTreeFrame> frames,
        int start,
        int end,
        StringBuilder sb,
        Func<ulong, string, string?> registerEvent)
    {
        var i = start;
        while (i < end)
        {
            ref var frame = ref frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                {
                    var name = frame.ElementName;
                    var subtreeEnd = i + frame.ElementSubtreeLength;

                    sb.Append('<').Append(name);

                    // Attributes are the frames immediately after the element, before its children.
                    var child = i + 1;
                    while (child < subtreeEnd && frames.Array[child].FrameType == RenderTreeFrameType.Attribute)
                    {
                        WriteAttribute(ref frames.Array[child], sb, registerEvent);
                        child++;
                    }

                    if (VoidElements.Contains(name))
                    {
                        sb.Append('>');
                    }
                    else
                    {
                        sb.Append('>');
                        WriteRange(renderer, frames, child, subtreeEnd, sb, registerEvent);
                        sb.Append("</").Append(name).Append('>');
                    }

                    i = subtreeEnd;
                    break;
                }

                case RenderTreeFrameType.Text:
                    HtmlEncode(frame.TextContent, sb);
                    i++;
                    break;

                case RenderTreeFrameType.Markup:
                    // Already markup — this is how Rask children cross in, so it must stay verbatim.
                    sb.Append(frame.MarkupContent);
                    i++;
                    break;

                case RenderTreeFrameType.Component:
                {
                    // A nested Blazor component keeps its own frame buffer.
                    WriteComponent(renderer, frame.ComponentId, sb, registerEvent);
                    i += frame.ComponentSubtreeLength;
                    break;
                }

                case RenderTreeFrameType.Region:
                    WriteRange(renderer, frames, i + 1, i + frame.RegionSubtreeLength, sb, registerEvent);
                    i += frame.RegionSubtreeLength;
                    break;

                default:
                    // ElementReferenceCapture, ComponentReferenceCapture, NamedEvent: nothing to write.
                    // A captured ElementReference is only meaningful to code running in the browser,
                    // which is precisely what a statically hosted component does not have.
                    i++;
                    break;
            }
        }
    }

    private static void WriteAttribute(
        ref RenderTreeFrame frame,
        StringBuilder sb,
        Func<ulong, string, string?> registerEvent)
    {
        var name = frame.AttributeName;

        if (frame.AttributeEventHandlerId != 0)
        {
            // "onclick" -> "click", so it lands on Rask's own data-rask-on-{event} convention and the
            // delegated listener already in the page picks it up with no new client code.
            var eventName = name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ? name[2..] : name;
            if (registerEvent(frame.AttributeEventHandlerId, eventName) is { } raskId)
            {
                sb.Append(" data-rask-on-").Append(eventName).Append("=\"").Append(raskId).Append('"');
            }

            return;
        }

        switch (frame.AttributeValue)
        {
            case null:
                break;
            case bool b:
                // Blazor's convention: false removes the attribute, true writes it bare.
                if (b)
                {
                    sb.Append(' ').Append(name);
                }

                break;
            default:
                sb.Append(' ').Append(name).Append("=\"");
                HtmlEncodeAttribute(frame.AttributeValue.ToString(), sb);
                sb.Append('"');
                break;
        }
    }

    private static void HtmlEncode(string? value, StringBuilder sb)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    private static void HtmlEncodeAttribute(string? value, StringBuilder sb)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
    }
}
