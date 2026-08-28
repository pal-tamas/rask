using System.Text;
using Rask.Core;

namespace Rask.External;

/// <summary>
///     A Rask component whose markup is produced by a front-end framework rather than by Rask.
/// </summary>
/// <remarks>
///     <para>
///         Derive from <see cref="ReactComponent" /> or <see cref="LitComponent" /> rather than from
///         this directly — the runtime has to be known at compile time, because the build needs it to
///         pair the component with an adapter, and naming it in the base class is the one place it
///         cannot drift from what actually mounts.
///     </para>
///     <para>
///         Everything that makes a component externally rendered lives here rather than in generated
///         code: the host element, the opaque-subtree boundary, the slot grouping, the
///         <c>&lt;script&gt;</c> that boots the client runtime, and the attribute writer. A generator
///         emits only the three things it alone can know — the component's name, the module beside it,
///         and a reflection-free writer for its props.
///     </para>
///     <para>
///         Because the runtime is a base class rather than an attribute, a component that already
///         extends something else cannot be externally rendered. That is deliberate: chrome in Rask comes
///         from the chain, not from inheritance, so the answer is to compose —
///         <c>BsCard[ Chart.Series(points) ]</c> — rather than to inherit both.
///     </para>
/// </remarks>
public abstract partial class ExternalComponent : Component
{
    /// <summary>When the adapter mounts this component. Unset means <c>Load</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         Nullable rather than a non-nullable property with an initializer, and that is not a style
    ///         choice. A member initializer compiles into the constructor and leaves no trace in
    ///         metadata, so from the app's compilation — which inherits this property across an assembly
    ///         boundary — <c>Hydration { get; set; } = Load</c> is indistinguishable from
    ///         <c>Hydration { get; set; }</c>. The chain would read it as a REQUIRED step and force every
    ///         call site in every app to spell out <c>.Hydration(...)</c> for a value that has an obvious
    ///         default.
    ///     </para>
    ///     <para>
    ///         Nullability survives metadata, so it says the same thing on both sides — and it says it
    ///         more precisely: null is "not specified", which is exactly what the client already treats
    ///         a missing <c>hydrate</c> attribute as.
    ///     </para>
    /// </remarks>
    public ExternalHydration? Hydration { get; set; }

    /// <summary>Which adapter mounts this component. Fixed by the base class it derives from.</summary>
    protected abstract string Runtime { get; }

    /// <summary>
    ///     The front-end module, as the browser imports it.
    /// </summary>
    /// <remarks>
    ///     Generated as the sibling file — <c>Chart.cs</c> pairs with <c>Chart.tsx</c>, the way scoped
    ///     CSS and scoped JS already pair. Override it with a <em>constant</em> string to point
    ///     somewhere convention cannot reach; the bundler reads the value at build time, so anything it
    ///     cannot evaluate is <see href="https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md#rask059">RASK059</see>.
    /// </remarks>
    protected abstract string Module { get; }

    /// <summary>The name the client runtime resolves a module by. Generated from the type name.</summary>
    protected abstract string ComponentName { get; }

    /// <summary>The host element a front-end framework mounts into.</summary>
    protected sealed override string? TagName => ExternalDefaults.HostTag;

    /// <summary>Everything below this element belongs to the front-end framework that rendered it.</summary>
    protected sealed override bool OpaqueSubtree => true;

    /// <summary>Boots the client runtime. Deduplicated across every such component on the page.</summary>
    protected override Component? HeadAssets =>
        Script.Src(ExternalDefaults.RuntimeScriptUrl).Type("module");

    /// <summary>
    ///     Never called: the serializer takes its element branch the moment <see cref="TagName" /> is
    ///     non-null, and the markup comes from the front-end file.
    /// </summary>
    /// <remarks>
    ///     Sealed rather than merely overridden, so that writing a <c>Render()</c> on an external
    ///     component is a compile error the compiler itself reports (CS0239). Left open it would read as
    ///     the component's markup while having no effect at all, which is worse than either behaviour on
    ///     its own — this used to need an analyzer to say so.
    /// </remarks>
    protected sealed override Component? Render() => null;

    /// <summary>Groups children into the slot templates the adapter mounts them from.</summary>
    /// <remarks>
    ///     Reached because an opaque component takes the serializer's virtual children walk rather than
    ///     its <c>ChildrenArray</c> fast path — that path skips <c>RenderChildren</c> entirely, and the
    ///     indexer produces exactly the array it fast-paths on.
    /// </remarks>
    protected sealed override IEnumerable<Component?> RenderChildren() =>
        ExternalSlots.Wrap(Children);

    /// <summary>The props, as the JSON the client runtime hands to the adapter. Generated.</summary>
    protected abstract string WriteProps();

    /// <inheritdoc />
    protected sealed override void WriteAttributes(StringBuilder sb)
    {
        AppendAttr(sb, ExternalDefaults.NameAttribute, ComponentName);
        AppendAttr(sb, ExternalDefaults.ModuleAttribute, Module);
        AppendAttr(sb, ExternalDefaults.RuntimeAttribute, Runtime);

        if (Hydration is { } declared && ExternalDefaults.Wire(declared) is { } hydrate)
        {
            AppendAttr(sb, ExternalDefaults.HydrateAttribute, hydrate);
        }

        // AppendAttr, not sb.Append: it registers the Attribute frame as well as writing the markup.
        // Without the frame the value renders once and never diffs again, so a prop change would stop
        // reaching the adapter after the first paint.
        AppendAttr(sb, ExternalDefaults.PropsAttribute, WriteProps());
    }
}
