using System.Text;
using Rask.Core.Live;

namespace Rask.Core;

// HTML element base. Carries the universal HTML attributes (Id/Class/Style/Data) so that
// tag classes (Div, Span, Input, …) inherit them and their generated factories expose them
// as optional parameters. User components extend Component directly and stay free of these
// HTML-only concerns.
public abstract class Element : Component
{
    public string? Id { get; set; }
    public string? Class { get; set; }
    public string? Style { get; set; }
    public IReadOnlyDictionary<string, string?>? Data { get; set; }

    // A stable DOM handle for JS interop. When set, emits data-rask-ref="{id}" in the data-* group;
    // the client reviver resolves an ElementRef arg to this element via [data-rask-ref="..."].
    // Storage is hoisted into the lazy LiveState (ElementRefInternal) so a ref-less element keeps
    // `_live` null and adds zero footprint — direct fields on Element are what the LiveState hoist
    // exists to avoid. The generator special-cases ElementRef to an optional factory parameter
    // (Blazor @ref parity, available on every element).
    public ElementRef? Ref
    {
        get => ElementRefInternal;
        set => ElementRefInternal = value;
    }

    // Native HTML5 drag-and-drop, available on every element. `Draggable` emits draggable="true"
    // (nullable so it stays an optional factory param — Blazor-parity with the other HTML attrs);
    // the four handlers bind the dragstart/dragover/drop/dragend DOM events to parameterless C#
    // delegates (Action or Func<Task>), wired by the client runtime via data-rask-on-drag*. The
    // dragged item's identity is carried by the handler's closure, not the event payload — see
    // the headless DragDrop primitive (DragDrop.cs) and the DragDropContext it hands consumers.
    public bool? Draggable { get; set; }
    public Delegate? OnDragStart { get; set; }
    public Delegate? OnDragOver { get; set; }
    public Delegate? OnDrop { get; set; }
    public Delegate? OnDragEnd { get; set; }

    // Subclasses transform the `class` attribute value without re-implementing the universal
    // id/class/style/data-* walk. NavLink overrides this to splice in its active class.
    protected virtual string? ResolveClass() => Class;

    protected override void WriteAttributes(StringBuilder sb)
    {
        if (Id is not null)
        {
            AppendAttr(sb, "id", Id);
        }

        var cls = ResolveClass();
        if (cls is not null)
        {
            AppendAttr(sb, "class", cls);
        }

        if (Style is not null)
        {
            AppendAttr(sb, "style", Style);
        }

        // Effective keyed-list identity: this element's own Key, else a key forwarded from a
        // transparent ancestor component (Consume clears the slot so only the FIRST element
        // adopts it). Emitted in the data-* group below so FrameDiffer.ExtractRaskKey finds it
        // among the leading attribute frames, same as a Data["rask-key"] entry.
        var forwarded = KeyForwardScope.Consume();
        var key = KeyString ?? forwarded;

        if (Data is not null)
        {
            foreach (var kv in Data)
            {
                // A literal Data["rask-key"] is superseded by an effective Key to avoid a
                // duplicate attribute — Key is the canonical API; Data stays for back-compat.
                if (key is not null && string.Equals(kv.Key, "rask-key", StringComparison.Ordinal))
                {
                    continue;
                }

                AppendAttr(sb, "data-", kv.Key, kv.Value);
            }
        }

        if (key is not null)
        {
            AppendAttr(sb, "data-", "rask-key", key);
        }

        // Element ref handle (JS interop): a data-* attribute, emitted alongside rask-key so it
        // sits in the universal data-* group, before drag hooks and tag-specifics.
        if (Ref is { } elementRef)
        {
            AppendAttr(sb, "data-", "rask-ref", elementRef.Id);
        }

        // Drag-and-drop: a universal attribute (draggable) plus the data-rask-on-drag* handler
        // hooks. Emitted here in the universal section, before subclass tag-specifics (which run
        // after base.WriteAttributes). Unset (null / no handler) emits nothing.
        if (Draggable is true)
        {
            AppendAttr(sb, "draggable", "true");
        }

        if (LiveRenderContext.CurrentSync is { } ctx)
        {
            if (OnDragStart is not null)
            {
                AppendAttr(sb, "data-rask-on-dragstart", ctx.RegisterHandler(OnDragStart));
            }

            if (OnDragOver is not null)
            {
                AppendAttr(sb, "data-rask-on-dragover", ctx.RegisterHandler(OnDragOver));
            }

            if (OnDrop is not null)
            {
                AppendAttr(sb, "data-rask-on-drop", ctx.RegisterHandler(OnDrop));
            }

            if (OnDragEnd is not null)
            {
                AppendAttr(sb, "data-rask-on-dragend", ctx.RegisterHandler(OnDragEnd));
            }
        }
    }
}
