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

    // The events Rask feeds a parameterless handler (HandlerFrameShape's "None" row). A Func<Task>
    // registered for anything else is refused at dispatch, because the inbound frame's type belongs
    // to a shape that wants arguments.
    private static readonly HashSet<string> ParameterlessEvents = new(StringComparer.Ordinal)
    {
        "click", "dragstart", "dragover", "drop", "dragend", "drag", "dragenter", "dragleave",
        "focus", "blur", "focusin", "focusout", "select", "invalid", "reset",
    };

    // The events that carry the element's value, fed to an Action<string>/Func<string, Task>.
    private static readonly HashSet<string> ValueEvents = new(StringComparer.Ordinal)
    {
        "change", "input",
    };

    // Attributes whose value is a URL, and so a script-execution sink: javascript: and vbscript: run
    // on click, data: can carry a document. Rask neutralises these framework-wide through
    // Component.AppendUrlAttr, and an island's markup reaches the page through Raw — Rask's only
    // un-encoded path — so nothing downstream would catch a miss here.
    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "action", "formaction", "ping", "cite", "background",
    };

    // As above, but inline media is both common and inert, so data:image/* and friends stay allowed.
    private static readonly HashSet<string> MediaUrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "poster", "srcset",
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

            // An event Rask cannot route to a handler of the shape we register gets NO attribute.
            // Rask matches an inbound frame to a handler by the delegate's shape, and refuses a
            // mismatch — so emitting the attribute anyway would render a component that looks wired
            // and does nothing on the first click, which is the failure this package exists to avoid.
            if (!ValueEvents.Contains(eventName) && !ParameterlessEvents.Contains(eventName))
            {
                return;
            }

            if (registerEvent(frame.AttributeEventHandlerId, eventName) is not { } raskId)
            {
                return;
            }

            // A value-carrying event goes through Rask's INPUT channel rather than its DOM-event one,
            // and that distinction is what makes @bind work: `change` and `input` are deliberately
            // absent from the DOM-event table because the client reads the element's value and ships
            // it alongside the id.
            //
            // ONE attribute, never both. They carry separate ids and each dispatches its own frame,
            // so writing the same id to both fires the handler twice per edit — harmless for @bind,
            // wrong for a hosted @onchange that appends to a list or increments a counter.
            sb.Append(" data-rask-on-").Append(eventName).Append("=\"").Append(raskId).Append('"');
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
            {
                var text = frame.AttributeValue.ToString();

                // A URL-valued attribute goes through the same sanitizer every Rask element uses, so
                // a hosted <a href="@Url"> fed from a parameter cannot emit javascript: verbatim.
                if (UrlAttributes.Contains(name))
                {
                    text = UrlSanitizer.Sanitize(text);
                }
                else if (MediaUrlAttributes.Contains(name))
                {
                    text = UrlSanitizer.SanitizeMedia(text);
                }

                sb.Append(' ').Append(name).Append("=\"");
                HtmlEncode(text, sb);
                sb.Append('"');
                break;
            }
        }
    }

    /// <summary>
    ///     Appends <paramref name="value" /> HTML-encoded, by the same encoder the rest of Rask uses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>HtmlSerializer.AppendEncoded</c> rather than a switch over the handful of characters
    ///         that obviously matter. This is an XSS-relevant path, and a hand-rolled encoder here
    ///         would be a second, untested answer to a question the framework already answers — it
    ///         delegates to <c>HtmlEncoder.Default</c>, which is stricter than the obvious set (it
    ///         encodes <c>'</c> and non-ASCII too) behind a fast path that leaves safe ASCII
    ///         untouched with no allocation.
    ///     </para>
    ///     <para>
    ///         One encoder for both text and attribute values, because <c>HtmlEncoder.Default</c>
    ///         escapes the quote as well — so there is no context this is too weak for, and no second
    ///         rule to keep in step with the first.
    ///     </para>
    /// </remarks>
    private static void HtmlEncode(string? value, StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(value))
        {
            HtmlSerializer.AppendEncoded(sb, value);
        }
    }
}
