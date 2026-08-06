using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Core.Live;

// Cross-checks the `type` a client frame declares against the argument shape the handler its `id`
// resolved to actually demands.
//
// Handler ids are POSITIONAL per render (Component.HandlerId over Live.NextHandlerId), so the same id
// names a different handler after the tree changes. Dispatch used to key on the id alone: a frame that
// outlived the render it was issued against resolved to whatever now sits in that slot and ran it, with
// no complaint — `{"id":"h37","type":"input","value":"…"}` arriving at a page where h37 is now a
// parameterless Callback invoked that callback. Not a cross-origin hole (the socket is same-origin and
// session-bound), but positional ids make the collision ordinary rather than exotic, and the silence is
// what makes it bad: nothing says the wrong thing ran.
//
// The check is the frame's own claim about what it carries, versus what the delegate needs to be fed. A
// mismatch is a stale id by definition, so it is answered exactly like one — `false`, no render.
//
// Deliberately NOT a whitelist: a type this build has never heard of is ALLOWED through. A browser
// holding a cached client from another deploy must not have its events silently swallowed; the point is
// to refuse frames that are provably for a different kind of handler, not to police the vocabulary.
// This does mean two events of the same shape (a `focus` frame against a `click` handler — both
// parameterless) still pass. Telling those apart needs the event NAME carried per handler, which costs a
// reference per live handler in every session; that trade is not worth it for two events whose payloads
// are both empty.
internal static class HandlerFrameShape
{
    /// <summary>The argument a handler demands — equivalently, the payload a frame has to carry to feed it.</summary>
    internal enum Shape
    {
        None = 0,
        Modifiers,
        Value,
        Form,
        Files,
        Scroll,
        Keyboard,
        Mouse,
        Wheel,
        Pointer,
        Touch,
        Clipboard,
        Media
    }

    // The frame types that legitimately feed each shape, indexed by (int)Shape. Rows overlap on purpose:
    // `click` feeds a parameterless handler, the legacy MouseModifiers one, and a MouseEventArgs one, so
    // it appears in three rows. Kept in step with the client's send sites (rask.js / rask.wasm.js /
    // rask.native.js + the shared rask-input.js / rask-events.js splices) and with the delegate cases in
    // Component.TryInvokeHandlerAsync.
    //
    // Held as UTF-8 so the comparison runs against the frame's raw bytes — JsonElement.ValueEquals over a
    // byte span, the same shape the inbound type routing uses, so no frame type is ever materialised as a
    // string. Built once at class init.
    private static readonly byte[][][] Feeders =
    {
        // None — parameterless. The frames that carry nothing beyond their id.
        new[]
        {
            "click"u8.ToArray(), "dragstart"u8.ToArray(), "dragover"u8.ToArray(), "drop"u8.ToArray(),
            "dragend"u8.ToArray(), "drag"u8.ToArray(), "dragenter"u8.ToArray(), "dragleave"u8.ToArray(),
            "focus"u8.ToArray(), "blur"u8.ToArray(), "focusin"u8.ToArray(), "focusout"u8.ToArray(),
            "select"u8.ToArray(), "invalid"u8.ToArray(), "reset"u8.ToArray()
        },
        // Modifiers — MouseModifiers (click only).
        new[] { "click"u8.ToArray() },
        // Value — the string payload.
        new[] { "input"u8.ToArray(), "change"u8.ToArray(), "beforeinput"u8.ToArray() },
        // Form — FormData.
        new[] { "submit"u8.ToArray() },
        // Files — the uploaded-file metadata.
        new[] { "files"u8.ToArray() },
        // Scroll — ScrollEvent.
        new[] { "scroll"u8.ToArray() },
        // Keyboard — KeyboardEventArgs.
        new[] { "keydown"u8.ToArray(), "keyup"u8.ToArray() },
        // Mouse — MouseEventArgs.
        new[]
        {
            "click"u8.ToArray(), "dblclick"u8.ToArray(), "mousedown"u8.ToArray(), "mouseup"u8.ToArray(),
            "mousemove"u8.ToArray(), "mouseenter"u8.ToArray(), "mouseleave"u8.ToArray(),
            "mouseover"u8.ToArray(), "mouseout"u8.ToArray(), "contextmenu"u8.ToArray()
        },
        // Wheel — WheelEventArgs.
        new[] { "wheel"u8.ToArray() },
        // Pointer — PointerEventArgs.
        new[]
        {
            "pointerdown"u8.ToArray(), "pointerup"u8.ToArray(), "pointermove"u8.ToArray(),
            "pointerenter"u8.ToArray(), "pointerleave"u8.ToArray(), "pointerover"u8.ToArray(),
            "pointerout"u8.ToArray(), "pointercancel"u8.ToArray()
        },
        // Touch — TouchEventArgs.
        new[]
        {
            "touchstart"u8.ToArray(), "touchend"u8.ToArray(),
            "touchmove"u8.ToArray(), "touchcancel"u8.ToArray()
        },
        // Clipboard — ClipboardEventArgs.
        new[] { "copy"u8.ToArray(), "cut"u8.ToArray(), "paste"u8.ToArray() },
        // Media — MediaEventArgs.
        new[]
        {
            "play"u8.ToArray(), "pause"u8.ToArray(), "playing"u8.ToArray(), "ended"u8.ToArray(),
            "timeupdate"u8.ToArray(), "volumechange"u8.ToArray(), "ratechange"u8.ToArray(),
            "durationchange"u8.ToArray(), "loadedmetadata"u8.ToArray(), "seeked"u8.ToArray(),
            "seeking"u8.ToArray(), "waiting"u8.ToArray()
        }
    };

    /// <summary>
    ///     Whether <paramref name="handler" /> may be invoked for this frame. True when the frame declares
    ///     no type (a host that doesn't tag frames, or a direct <c>RaskTest</c> dispatch), when the type
    ///     feeds the handler's shape, or when no shape claims the type at all.
    /// </summary>
    public static bool Accepts(JsonElement payload, Delegate handler)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        // The happy path is one row: the shape the handler demands, scanned against the frame's raw
        // UTF-8. Nothing is allocated, and nothing else is consulted unless the frame is refused.
        if (Contains(Feeders[(int)ShapeOf(handler)], type))
        {
            return true;
        }

        // Not a feeder for this shape. Refuse only if some OTHER shape claims it; an unrecognised type
        // is a client this build doesn't know, not a misfire.
        foreach (var row in Feeders)
        {
            if (Contains(row, type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(byte[][] types, JsonElement type)
    {
        foreach (var candidate in types)
        {
            if (type.ValueEquals(candidate))
            {
                return true;
            }
        }

        return false;
    }

    // Mirrors the delegate cases of Component.TryInvokeHandlerAsync one-for-one. Anything it doesn't
    // recognise is parameterless as far as dispatch is concerned: the switch's `default` arm reaches it
    // through DynamicInvoke() with NO arguments, so a data-carrying frame has nothing to give it.
    private static Shape ShapeOf(Delegate handler) => handler switch
    {
        Action or Func<Task> or Callback or CallbackAsync => Shape.None,
        Action<MouseModifiers> or Func<MouseModifiers, Task>
            or Callback<MouseModifiers> or CallbackAsync<MouseModifiers> => Shape.Modifiers,
        Action<string> or Func<string, Task> or Callback<string> or CallbackAsync<string> => Shape.Value,
        Action<FormData> or Func<FormData, Task> or Callback<FormData> or CallbackAsync<FormData> => Shape.Form,
        Action<IReadOnlyList<RaskFile>> or Func<IReadOnlyList<RaskFile>, Task>
            or Callback<IReadOnlyList<RaskFile>> or CallbackAsync<IReadOnlyList<RaskFile>> => Shape.Files,
        Action<ScrollEvent> or Func<ScrollEvent, Task>
            or Callback<ScrollEvent> or CallbackAsync<ScrollEvent> => Shape.Scroll,
        Action<KeyboardEventArgs> or Func<KeyboardEventArgs, Task>
            or Callback<KeyboardEventArgs> or CallbackAsync<KeyboardEventArgs> => Shape.Keyboard,
        Callback<MouseEventArgs> or CallbackAsync<MouseEventArgs> => Shape.Mouse,
        Callback<WheelEventArgs> or CallbackAsync<WheelEventArgs> => Shape.Wheel,
        Callback<PointerEventArgs> or CallbackAsync<PointerEventArgs> => Shape.Pointer,
        Callback<TouchEventArgs> or CallbackAsync<TouchEventArgs> => Shape.Touch,
        Callback<ClipboardEventArgs> or CallbackAsync<ClipboardEventArgs> => Shape.Clipboard,
        Callback<MediaEventArgs> or CallbackAsync<MediaEventArgs> => Shape.Media,
        _ => Shape.None
    };
}
