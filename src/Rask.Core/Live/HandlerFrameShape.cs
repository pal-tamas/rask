using System.Text;
using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Core.Live;

// Cross-checks the `type` a client frame declares against the argument shape the handler its `id`
// resolved to actually demands.
//
// A handler id names one component's slot (Component.RegisterHandler), so the same id can still name a
// different handler after that component changes what it renders into that slot. Dispatch used to key
// on the id alone: a frame that outlived the render it was issued against ran whatever now sits there,
// with no complaint — `{"id":"h37","type":"input","value":"…"}` arriving at a page where h37 is now a
// parameterless Action invoked that callback. Not a cross-origin hole (the socket is same-origin and
// session-bound), and per-component ids make it far rarer than the page-wide counter that preceded them
// did — a slot is no longer reassigned just because something upstream changed. The silence is what
// makes the remainder bad: nothing says the wrong thing ran.
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
    /// <summary>The argument a non-DOM handler demands — equivalently, the payload a frame has to carry to feed it.</summary>
    internal enum Shape
    {
        None = 0,
        Value,
        Values,
        Form,
        Files,
    }

    // The frames that feed each non-DOM shape, indexed by (int)Shape: a typed control's own input/change, a form's
    // submit, a file input's files. Every DOM event (click, keydown, pointermove, …) is judged by the generated
    // DomEventDispatch instead, from MDN's interfaces: a handler taking T is fed the events whose interface is T or
    // derives from it, and a handler taking nothing is fed any of them.
    //
    // Held as UTF-8 so the comparison runs against the frame's raw bytes — JsonElement.ValueEquals over a byte
    // span, the same shape the inbound type routing uses, so no frame type is ever materialised as a string.
    private static readonly byte[][][] Feeders =
    {
        Array.Empty<byte[]>(),
        new[] { "input"u8.ToArray(), "change"u8.ToArray() },
        new[] { "change"u8.ToArray() },
        new[] { "submit"u8.ToArray() },
        new[] { "files"u8.ToArray() },
    };

    public static bool Accepts(JsonElement payload, Delegate handler)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        var shape = ShapeOf(handler);
        var dom = DomEventDispatch.IndexOf(type);
        if (dom >= 0)
        {
            // A DOM event frame: a DOM event handler (or a parameterless one) is judged by its interface; a handler
            // for a control's value, a form or files is provably the wrong one; anything else is not ours to refuse.
            return shape == Shape.None ? DomEventDispatch.Accepts(dom, handler) || !DomEventDispatch.IsDomHandler(handler) : false;
        }

        if (Contains(Feeders[(int)shape], type))
        {
            return true;
        }

        foreach (var row in Feeders)
        {
            if (Contains(row, type))
            {
                return false;
            }
        }

        return true;
    }

    // Whether a parameterless handler can be fed an event of this name: every DOM event can (BlazorFrameWriter asks
    // before it wires a hosted component's @onclick-style handler).
    internal static bool FeedsParameterless(string eventName) => DomEventDispatch.Names.Contains(eventName);

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

    // The Values arm is last on purpose: IReadOnlyList<string> is the widest match, and every narrower shape
    // above it must win first.
    private static Shape ShapeOf(Delegate handler) => handler switch
    {
        Action<string> or Func<string, Task> => Shape.Value,
        Action<FormData> or Func<FormData, Task> => Shape.Form,
        Action<IReadOnlyList<RaskFile>> or Func<IReadOnlyList<RaskFile>, Task> => Shape.Files,
        Action<IReadOnlyList<string>> or Func<IReadOnlyList<string>, Task> => Shape.Values,
        _ => Shape.None,
    };
}
