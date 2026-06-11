using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.HeadAssets;
using Rask.Core.Live;

namespace Rask.Core;

internal static class HtmlSerializer
{
    // Chars guaranteed to be left alone by HtmlEncoder.Default. The default encoder
    // is XSS-conservative — it encodes everything outside this narrow safe list,
    // including '+', '[', ']', '{', '}', ';', etc. plus any non-ASCII. Matching the
    // encoder's *actual* output requires staying inside this set. The intersection
    // covers the vast majority of attribute values and text content typical of a
    // Rask render (ids, classes, label text, paths, numbers).
    internal static readonly SearchValues<char> SafeAsciiForHtml = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyz" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "0123456789" +
        " -._/:,?!()*=#%");

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

    /// <summary>
    ///     Append <paramref name="value" /> to <paramref name="sb" /> HTML-encoded.
    ///     Fast path: when every char is in the safe-ASCII set the
    ///     <see cref="HtmlEncoder.Default" /> output is byte-identical to the input,
    ///     so we skip the encoder call (and its per-call string allocation) entirely.
    ///     Anything outside the safe set — including '+', '[', ']', '{', '}', ';',
    ///     non-ASCII — falls through to the encoder. Output matches the prior
    ///     <c>sb.Append(HtmlEncoder.Default.Encode(value))</c> sequence exactly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AppendEncoded(StringBuilder sb, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (value.AsSpan().IndexOfAnyExcept(SafeAsciiForHtml) < 0)
        {
            sb.Append(value);
            return;
        }

        sb.Append(HtmlEncoder.Default.Encode(value));
    }

    public static void Serialize(Component component, StringBuilder sb)
    {
        // FrameSinkScope.Current is the ambient frame writer set by the caller when it
        // wants a parallel RenderFrame[] alongside the HTML output (Phase 1 diff codec).
        // Reading it once per call and threading it through avoids the ThreadStatic
        // re-read inside every branch; keep the variable in a register hot.
        var frames = FrameSinkScope.Current;

        switch (component)
        {
            case Text t:
            {
                var textStart = sb.Length;
                AppendEncoded(sb, t.Value ?? string.Empty);
                frames?.Text(t.Value, textStart, sb.Length);
                break;
            }

            case Raw r:
            {
                var rawStart = sb.Length;
                sb.Append(r.Value);
                frames?.Raw(r.Value, rawStart, sb.Length);
                break;
            }

            case Doctype:
            {
                var doctypeStart = sb.Length;
                sb.Append("<!DOCTYPE html>");
                frames?.Doctype(doctypeStart, sb.Length);
                break;
            }

            case Fragment fragment:
            {
                // A keyed Fragment forwards its Key onto the first element it renders (the
                // first child's first element); Consume in WriteAttributes claims it once.
                var fragKey = fragment.KeyString;
                if (fragKey is not null)
                {
                    KeyForwardScope.Arm(fragKey);
                }

                try
                {
                    if (fragment.ChildrenArray is { } fragmentArray)
                    {
                        for (var i = 0; i < fragmentArray.Length; i++)
                        {
                            Serialize(fragmentArray[i].Component, sb);
                        }
                    }
                    else if (fragment.Children is { } fragmentChildren)
                    {
                        foreach (var child in fragmentChildren)
                        {
                            Serialize(child.Component, sb);
                        }
                    }
                }
                finally
                {
                    if (fragKey is not null)
                    {
                        KeyForwardScope.Clear();
                    }
                }

                break;
            }

            case ErrorBoundary boundary:
                SerializeErrorBoundary(boundary, sb);
                break;

            case Context context:
            {
                // Transparent like Fragment: push the provided value onto the ambient stack for
                // the duration of the children walk, so any descendant's Render() (which runs
                // inside this synchronous walk) resolves it via Context.Get<T>(). A keyed
                // provider forwards its Key onto the first element its children render.
                var ctxKey = context.KeyString;
                if (ctxKey is not null)
                {
                    KeyForwardScope.Arm(ctxKey);
                }

                using (ContextStack.Push(context.ValueType, context.Name, context.Value))
                {
                    try
                    {
                        if (context.ChildrenArray is { } ctxArray)
                        {
                            for (var i = 0; i < ctxArray.Length; i++)
                            {
                                Serialize(ctxArray[i].Component, sb);
                            }
                        }
                        else if (context.Children is { } ctxChildren)
                        {
                            foreach (var child in ctxChildren)
                            {
                                Serialize(child.Component, sb);
                            }
                        }
                    }
                    finally
                    {
                        if (ctxKey is not null)
                        {
                            KeyForwardScope.Clear();
                        }
                    }
                }

                break;
            }

            case { TagNameInternal: { } tagName } el:
                var live = LiveRenderContext.CurrentSync;
                var scopeId = live?.CurrentScopeId;
                var isShell = _shellTags.Contains(tagName);
                var elementStart = sb.Length;
                var elementFrameIdx = frames?.OpenElement(tagName,
                    scopeId is not null && !isShell ? scopeId : null,
                    el.SelfClosingInternal,
                    elementStart) ?? -1;

                sb.Append('<').Append(tagName);
                el.WriteAttributesInternal(sb);

                if (scopeId is not null && !isShell)
                {
                    sb.Append(" data-").Append(scopeId);
                }

                if (el.SelfClosingInternal)
                {
                    sb.Append(" />");
                    if (frames is not null)
                    {
                        frames.CloseElement(elementFrameIdx, sb.Length);
                    }

                    break;
                }

                sb.Append('>');
                using (el.EnterChildrenScopeInternal())
                {
                    if (el.ChildrenArray is { } childArray)
                    {
                        // Index walk over the backing Child[] — no enumerator allocation.
                        for (var i = 0; i < childArray.Length; i++)
                        {
                            Serialize(childArray[i].Component, sb);
                        }
                    }
                    else
                    {
                        foreach (var child in el.RenderChildrenInternal())
                        {
                            Serialize(child.Component, sb);
                        }
                    }
                }

                // The <head> element is framework-managed: emit the head-asset sentinel
                // here so user-declared Head contributions (collected during the render
                // walk) splice in via HeadAssetRegistry.ApplyTo, alongside scoped-css
                // and scoped-js framework markers. Children passed to Head() (if any
                // slip past the RASK019 analyzer) render first; the sentinel follows.
                if (tagName == "head" && live is not null)
                {
                    sb.Append(HeadAssetRegistry.Sentinel);
                }

                // The <body> element is framework-managed for the live runtime <script>:
                // resolve the host-registered IRaskRuntimeScript and emit its tag as the
                // last body child, so the user no longer hand-places RaskRuntimeScript().
                // Serialized inline (not via a post-process sentinel) so the script is
                // present and byte-stable on EVERY render — server live-updates re-serialize
                // and re-extract <body> without re-running RenderAsLiveRoot's post-processing,
                // so a sentinel would drop the script on the first update. Stable bytes also
                // mean the diff codec never emits ops for it. On WASM no provider is
                // registered (the runtime boots from the page shell), so nothing is injected.
                if (tagName == "body" && live?.Services?.GetService<IRaskRuntimeScript>() is { } runtime
                                      && runtime.Render() is { } runtimeScript)
                {
                    Serialize(runtimeScript, sb);
                }

                sb.Append("</").Append(tagName).Append('>');
                if (frames is not null)
                {
                    frames.CloseElement(elementFrameIdx, sb.Length);
                }

                break;

            default:
                // Push the parent scope for the entire duration of serialising this user
                // component — including the walk of its rendered subtree. That way
                // factories called from inside its Render AND handlers registered on
                // elements deep in its rendered tree both attribute back to this component.
                var liveCtx = LiveRenderContext.CurrentSync;
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

                // User components are transparent in the frame stream — their rendered
                // body's elements/text emit at the surrounding DOM level. That keeps the
                // diff codec's path computation a simple count over DOM-structural frames
                // without a Component-as-wrapper case. (A later optimisation may re-add
                // Component markers for cached-subtree short-circuiting.)
                using (LiveRenderContext.PushScopeOrNone(liveCtx, component))
                using (LiveRenderContext.EnterParentScopeOrNone(liveCtx, component))
                using (component.EnterChildrenScopeInternal())
                {
                    // Component-level Key (Blazor @key parity): a transparent user component
                    // emits no tag, so forward its Key onto the first element of its rendered
                    // body (the component's root element), which Consumes it in WriteAttributes.
                    // Only the first element claims it; Clear in finally stops it leaking to a
                    // following sibling when the body renders no element at all.
                    var fwdKey = component.KeyString;
                    if (fwdKey is not null)
                    {
                        KeyForwardScope.Arm(fwdKey);
                    }

                    try
                    {
                        Serialize(component.RenderForLive(), sb);
                    }
                    finally
                    {
                        if (fwdKey is not null)
                        {
                            KeyForwardScope.Clear();
                        }
                    }
                }

                break;
        }
    }

    private static void SerializeErrorBoundary(ErrorBoundary boundary, StringBuilder sb)
    {
        var live = LiveRenderContext.CurrentSync;
        using (LiveRenderContext.PushScopeOrNone(live, boundary))
        using (LiveRenderContext.EnterParentScopeOrNone(live, boundary))
        using (LiveRenderContext.PushBoundaryOrNone(live, boundary))
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
