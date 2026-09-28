using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// The events every element can fire. The properties themselves — OnClick, OnPointerMove, OnTimeUpdate, … —
// are generated from MDN's data into ElementEvents.g.cs (Rask.Dom.targets), with MDN's argument types and in the
// IDL's order; this half is the storage they share and the emit loop over them.
public abstract partial class Element
{
    // One backing store for every event an element can fire. A single keyed dictionary instead of ~100
    // named fields, kept as a DIRECT Element field (not hoisted into LiveState): a click-bearing
    // leaf would otherwise force a whole LiveState allocation, whereas this allocates only the small dict
    // on first handler. A plain element that wires nothing keeps `_domEvents` null and pays one extra
    // reference field. Each event name maps to ONE slot holding the delegate.
    //
    // The slot used to carry an IsAsync flag beside it, because an event was TWO properties — `OnClick`
    // and `OnClickAsync` — over this one slot, and a null re-applied by the factory had to clear only its
    // own kind. One `Callback` property per event makes both the flag and that asymmetry unnecessary: a
    // write is a write, and "both wired" is no longer expressible rather than diagnosed (RASK027).
    private Dictionary<string, Delegate>? _domEvents;

    // Render-hotpath early-out: WriteAttributes asks this before iterating the ordered event list. A
    // plain element answers false in one null check, so the per-render cost stays at zero.
    private protected bool HasDomEvents => _domEvents is { Count: > 0 };

    private protected Delegate? GetDomEvent(string name) =>
        _domEvents is { } map && map.TryGetValue(name, out var slot) ? slot : null;

    // ---- Typed views over the slot ----------------------------------------------------------------
    //
    // The dictionary holds every handler as a bare `Delegate`, because that is what dispatch needs, and
    // the properties below hand one back inside a `Callback` — a struct that HOLDS a delegate without
    // being one.
    //
    // That carrier is doing two jobs. It collapses the sync/async pair into one property, so an event has
    // one name and one slot and cannot be given two handlers. And because it is not a delegate type, it
    // cannot swallow a chain step of the same name: C# stops at a delegate-typed property when resolving
    // `x.OnClick(fn)` and reads the call as an invocation (CS1593), which is the whole reason the chain
    // had to receive one step off the component. A non-invocable member falls through to extension lookup
    // instead. See Rask.Core.Callback.
    //
    // Reading is total now rather than partial: a slot holding "the other kind" used to read back as null
    // through the `as` cast, and there is no other kind left.
    //
    // An empty slot reads back as the unset `Callback` — its default — rather than as null.
    private protected Callback Handler(string name) => new(GetDomEvent(name));

    private protected Callback<TArgs> Handler<TArgs>(string name) => new(GetDomEvent(name));

    // One writer, and a write is simply a write. There used to be two — a sync one that always won and an
    // async one that deferred to it — because an event was two properties over this one slot and the
    // runtime needed a tiebreaker for "both wired" (RASK027 reported the same thing at compile time).
    // With one property per event that state cannot be reached, so neither the tiebreaker nor the
    // clear-only-my-own-kind rule has anything left to arbitrate.
    private protected void SetHandler(string name, Delegate? value)
    {
        if (value is not null)
        {
            (_domEvents ??= new Dictionary<string, Delegate>(StringComparer.Ordinal))[name] = value;
        }
        else
        {
            _domEvents?.Remove(name);
        }
    }

    // Emits every wired GlobalEventHandlers hook as data-rask-on-{event}, in GlobalEventOrder, so the
    // serialized attribute sequence is deterministic. Early-outs in one null check for a plain element.
    internal void EmitDomEvents(StringBuilder sb, LiveRenderContext ctx)
    {
        if (!HasDomEvents)
        {
            return;
        }

        foreach (var name in GlobalEventOrder)
        {
            EmitDomEvent(sb, ctx, name);
        }
    }

    // Emits one event hook if a handler is wired for it.
    private protected void EmitDomEvent(StringBuilder sb, LiveRenderContext ctx, string name)
    {
        if (GetDomEvent(name) is { } handler)
        {
            AppendAttr(sb, "data-rask-on-", name, ctx.RegisterHandler(handler));
        }
    }
}
