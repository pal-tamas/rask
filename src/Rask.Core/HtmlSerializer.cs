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

    // Phase B: set true whenever a user component is serialized, so an enclosing component can tell
    // whether its subtree is pure elements (safe to cache + replay from frames) or contains a nested
    // user component that could go dirty independently (must keep the element-walk path). The default
    // switch branch saves/resets this around each component's walk to make it a per-subtree signal.
    [ThreadStatic] private static bool _sawNestedComponent;

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

    /// <summary>
    ///     Replay a clean component's retained <see cref="LeanFrame" /> subtree (Phase B): re-emit its
    ///     HTML byte-for-byte identical to the original <see cref="Serialize(Component, StringBuilder)" />
    ///     walk AND re-write the full <see cref="RenderFrame" /> stream (with fresh HTML offsets) into
    ///     <paramref name="writer" />, in a single pass — so a component that did not change re-emits from
    ///     a compact frame span instead of re-walking (and thus retaining) its Element object graph.
    ///     <para>
    ///         The span must be a well-formed subtree: it starts at a DOM-structural frame (Element /
    ///         Text / Raw / Doctype) and each Element frame's <see cref="LeanFrame.SubtreeLength" /> stays
    ///         within the span. Attribute frames are the leading children of their Element (as
    ///         <c>WriteAttributes</c> runs before children), consumed by the Element case, never at top
    ///         level. The span must NOT contain the shell tags (<c>head</c>/<c>body</c>) whose sentinel /
    ///         runtime script are appended without frames — those live only in the always-dirty root and
    ///         are never cached.
    ///     </para>
    /// </summary>
    internal static void ReplayLeanFrames(ReadOnlySpan<LeanFrame> frames, StringBuilder sb, FrameWriter writer)
    {
        var i = 0;
        while (i < frames.Length)
        {
            i = ReplayFrame(frames, i, sb, writer);
        }
    }

    private static int ReplayFrame(ReadOnlySpan<LeanFrame> frames, int i, StringBuilder sb, FrameWriter writer)
    {
        ref readonly var f = ref frames[i];
        switch (f.Kind)
        {
            case RenderFrameKind.Text:
            {
                // Text frames store the raw (un-encoded) value; re-encode on emit exactly as Serialize
                // did. Adjacent text was coalesced into one frame at capture, and HTML encoding is
                // per-char so the coalesced encode is byte-identical to the piecewise one.
                var start = sb.Length;
                AppendEncoded(sb, f.Name ?? string.Empty);
                writer.Text(f.Name, start, sb.Length);
                return i + 1;
            }

            case RenderFrameKind.Raw:
            {
                var start = sb.Length;
                sb.Append(f.Name);
                writer.Raw(f.Name, start, sb.Length);
                return i + 1;
            }

            case RenderFrameKind.Doctype:
            {
                var start = sb.Length;
                sb.Append("<!DOCTYPE html>");
                writer.Doctype(start, sb.Length);
                return i + 1;
            }

            case RenderFrameKind.Element:
            {
                var end = i + f.SubtreeLength;
                var start = sb.Length;
                var frameIdx = writer.OpenElement(f.Name!, f.Value, f.SelfClosing, start);
                sb.Append('<').Append(f.Name);

                // Leading Attribute frames = this element's attributes, in emit order.
                var j = i + 1;
                while (j < end && frames[j].Kind == RenderFrameKind.Attribute)
                {
                    ref readonly var a = ref frames[j];
                    sb.Append(' ').Append(a.Name);
                    if (a.Value is not null)
                    {
                        sb.Append("=\"");
                        AppendEncoded(sb, a.Value);
                        sb.Append('"');
                    }

                    writer.Attribute(a.Name!, a.Value);
                    j++;
                }

                // Scoped-CSS marker rides the Element frame's Value (Serialize appends it after the
                // real attributes and before '>' / ' />'), not an Attribute frame.
                if (f.Value is not null)
                {
                    sb.Append(" data-").Append(f.Value);
                }

                if (f.SelfClosing)
                {
                    sb.Append(" />");
                    writer.CloseElement(frameIdx, sb.Length);
                    return end;
                }

                sb.Append('>');
                while (j < end)
                {
                    j = ReplayFrame(frames, j, sb, writer);
                }

                sb.Append("</").Append(f.Name).Append('>');
                writer.CloseElement(frameIdx, sb.Length);
                return end;
            }

            default:
                // Attribute at top level (shouldn't happen for a well-formed subtree) or a Component
                // marker (never emitted into the stream). Skip defensively.
                return i + 1;
        }
    }

    public static void Serialize(Component? component, StringBuilder sb)
    {
        // A null component means "render nothing" — Render()/RenderForLive() return Component? and
        // yield null for the nothing-render case (formerly an empty Fragment). Emit nothing.
        if (component is null)
        {
            return;
        }

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
                            Serialize(fragmentArray[i], sb);
                        }
                    }
                    else if (fragment.Children is { } fragmentChildren)
                    {
                        foreach (var child in fragmentChildren)
                        {
                            Serialize(child, sb);
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
                                Serialize(ctxArray[i], sb);
                            }
                        }
                        else if (context.Children is { } ctxChildren)
                        {
                            foreach (var child in ctxChildren)
                            {
                                Serialize(child, sb);
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
                        // Index walk over the backing Component[] — no enumerator allocation.
                        for (var i = 0; i < childArray.Length; i++)
                        {
                            Serialize(childArray[i], sb);
                        }
                    }
                    else
                    {
                        foreach (var child in el.RenderChildrenInternal())
                        {
                            Serialize(child, sb);
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
                    // Record where the sentinel lands so RenderAsLiveRoot splices the head-asset
                    // block in place (StringBuilder.Insert) without re-scanning the whole page for
                    // the sentinel. The sentinel is byte-stable, so its start offset is all the
                    // splice needs. Record only the FIRST head — the framework shell renders exactly
                    // one <head> (RASK019/021), but a stray nested <head> from a component that
                    // slipped the analyzer would emit a second sentinel; first-wins matches the
                    // previous html.IndexOf(Sentinel) behaviour (splice the shell head, leave any
                    // duplicate sentinel as a literal) rather than mis-splicing the stray one.
                    if (live.HeadSentinelIndex < 0)
                    {
                        live.HeadSentinelIndex = sb.Length;
                    }

                    sb.Append(HeadAssetRegistry.Sentinel);

                    // Host-registered head markup (e.g. the Server PWA <link rel="manifest"> +
                    // <meta name="theme-color">), emitted as real HTML so no post-boot JS injection is
                    // needed. Serialized inline on every render and kept byte-stable, so — like the body
                    // runtime <script> — it survives server live-updates and costs the diff codec nothing.
                    // No provider registered (the default, and on WASM) → nothing extra is emitted.
                    if (live.Services?.GetService<IRaskHeadContribution>()?.Render() is { } headExtra)
                    {
                        Serialize(headExtra, sb);
                    }
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

                // Native header/footer collection — only when the host opts in (the native host with an
                // INativeChrome backend). Reading the overrides here, mid-walk, keeps the native factories
                // DI-correct (ambient context) and picks the deepest override (last non-null wins). Pure no-op
                // on Server/WASM: CollectsNativeChrome is false, so the overrides are never even read.
                if (liveCtx is not null && liveCtx.CollectsNativeChrome)
                {
                    liveCtx.CollectNativeChrome(component);
                }

                // Phase B: replay a cached clean subtree straight from its retained frame span instead
                // of re-walking (and thus retaining) its Element object graph. Only clean, pure-element,
                // handler-free subtrees were cached (see Component.TryCacheCleanSubtree); a dirty or
                // ineligible component returns false here and falls through to the walk below, which
                // re-renders it and may re-cache. A replayed component is itself a nested user component
                // for its parent, so flag that.
                if (frames is not null && component.TryReplayCleanSubtree(sb, frames, liveCtx))
                {
                    _sawNestedComponent = true;
                    break;
                }

                // User components are transparent in the frame stream — their rendered
                // body's elements/text emit at the surrounding DOM level. That keeps the
                // diff codec's path computation a simple count over DOM-structural frames
                // without a Component-as-wrapper case.
                //
                // Track whether THIS component's subtree contains any nested user component: reset the
                // ambient flag, walk, then read it back. A pure-element subtree (flag still false) is
                // safe to cache and replay; one with a nested component is not (the nested one could go
                // dirty on a later render and replaying the parent's frames would skip it). The flag is
                // re-asserted to true after the walk so an ENCLOSING component still sees us as nested.
                _sawNestedComponent = false;
                var frameStart = frames?.Count ?? -1;

                // Read the ambient forwarded key BEFORE the walk: if this component has no Key of its
                // own, its first element will Consume() an ancestor's pending key and bake it into the
                // frames we are about to capture. Reading it afterwards is too late — it has been
                // consumed — and the snapshot needs to know which identity it was captured under.
                var forwardedKeyAtCapture = KeyForwardScope.Peek();

                // Likewise the handler-id counter: the walk about to run issues this subtree's ids
                // starting here, and the snapshot has to know where its run began to replay it.
                var handlerStartId = liveCtx?.PeekNextHandlerId ?? 0;

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
                        var rendered = component.RenderForLive();

                        // Collect this component's Head contribution — produced by the render that just
                        // ran (Component.RenderForLive), which is what owns anything it built, and read
                        // back here rather than re-evaluated. Still before its subtree is walked, so the
                        // registry keeps walk order: an ancestor's head precedes every descendant's. The
                        // registry is consumed once at the end of RenderAsLiveRoot when it replaces the
                        // RaskHeadAssets sentinel — components that go away on the next render just stop
                        // contributing and their head tags fall out naturally.
                        if (liveCtx is not null && component.CachedHeadInternal is { } head)
                        {
                            liveCtx.HeadAssets.Add(head);
                        }

                        Serialize(rendered, sb);
                    }
                    finally
                    {
                        if (fwdKey is not null)
                        {
                            KeyForwardScope.Clear();
                        }
                    }
                }

                var hadNested = _sawNestedComponent;
                if (frames is not null && frameStart >= 0)
                {
                    component.TryCacheCleanSubtree(
                        frames, frameStart, hadNested, liveCtx?.CollectsNativeChrome ?? false,
                        forwardedKeyAtCapture, handlerStartId, liveCtx);
                }

                // Mark that our parent's subtree now contains a user component (us).
                _sawNestedComponent = true;

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
