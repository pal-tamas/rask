using System.Buffers;
using System.Text;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;

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

    /// <summary>
    ///     The manifest that resolves <see cref="ComponentName" /> to a chunk, or <c>null</c> for the
    ///     app's own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Virtual with a null default rather than abstract, so an island in an APP -- every island
    ///         there has ever been -- generates nothing and writes no attribute. It is only a class
    ///         library that needs it: a library's static web assets are served under
    ///         <c>_content/&lt;PackageId&gt;/</c>, so its bundle and manifest sit somewhere the client
    ///         cannot guess from the island's name.
    ///     </para>
    ///     <para>
    ///         Generated from <c>RaskExternalPublicBase</c>, which the build defaults per project kind.
    ///     </para>
    /// </remarks>
    protected virtual string? ManifestUrl => null;

    /// <summary>The host element a front-end framework mounts into.</summary>
    protected sealed override string? TagName => ExternalDefaults.HostTag;

    /// <summary>Everything below this element belongs to the front-end framework that rendered it.</summary>
    protected sealed override bool OpaqueSubtree => true;

    /// <summary>
    ///     Boots the client runtime. Deduplicated across every such component on the page.
    /// </summary>
    /// <remarks>
    ///     Registered from <see cref="WriteAttributes" /> rather than left for the serializer to
    ///     collect, and that is not a preference. The serializer only reads a component's head
    ///     contribution in its COMPONENT branch, right after <c>RenderForLive()</c>. Anything with a
    ///     <see cref="TagName" /> takes the element branch instead and never passes that code — so
    ///     this override, on its own, produced an empty <c>&lt;head&gt;</c>, no runtime script, and a
    ///     page where nothing could ever mount. It rendered perfectly and did nothing.
    ///
    ///     Kept as an override as well so the contribution is discoverable where a reader expects it.
    /// </remarks>
    protected override Component? HeadAssets =>
        Script.Src(ExternalDefaults.RuntimeScriptUrl).Type("module");

    /// <summary>
    ///     Puts the runtime script in the page's <c>&lt;head&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     The registry dedupes by the tag itself, so every component on the page can register the
    ///     same script and exactly one is emitted. Doing it here costs nothing for ordinary elements —
    ///     the alternative, reading <see cref="HeadAssets" /> for every element the serializer walks,
    ///     would add a virtual call per <c>&lt;div&gt;</c> to the render hot path for a case only this
    ///     component has.
    /// </remarks>
    private void RegisterRuntimeScript()
    {
        if (HeadAssets is { } script)
        {
            LiveRenderContext.CurrentSync?.HeadAssets.Add(script);
        }
    }

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

    /// <summary>
    ///     Writes the props as members of the JSON object the client runtime hands to the adapter. Generated.
    /// </summary>
    /// <remarks>
    ///     Members only: the object's braces, the writer and its buffer belong to this class. The generated
    ///     writer used to own all three and hand back a string, which left nothing else able to add to the
    ///     same object without re-parsing it — and an island's children travel in that object too.
    /// </remarks>
    /// <param name="writer">The writer, positioned inside the props object.</param>
    protected abstract void WriteProps(Utf8JsonWriter writer);

    /// <inheritdoc />
    protected sealed override void WriteAttributes(StringBuilder sb)
    {
        // An island is serialized down the element branch, so on its own it never tells the enclosing
        // component that its subtree holds a component. Left that way, a page of plain elements plus an
        // island is frame-cached, and its replay skips everything below: the runtime script this call
        // registers falls out of <head>, and the callbacks the props writer registers on the island's
        // own slots resolve to nothing for the rest of the session.
        LiveRenderContext.CurrentSync?.MarkSubtreeUncacheable();

        RegisterRuntimeScript();

        AppendAttr(sb, ExternalDefaults.NameAttribute, ComponentName);
        AppendAttr(sb, ExternalDefaults.ModuleAttribute, Module);
        AppendAttr(sb, ExternalDefaults.RuntimeAttribute, Runtime);

        // Only when it is not the app's own: the client already assumes that one, so writing it would
        // put the same string on every island on the page for no reader's benefit.
        if (ManifestUrl is { Length: > 0 } manifest
            && !string.Equals(manifest, ExternalDefaults.DefaultManifestUrl, StringComparison.Ordinal))
        {
            AppendAttr(sb, ExternalDefaults.ManifestAttribute, manifest);
        }

        if (Hydration is { } declared && ExternalDefaults.Wire(declared) is { } hydrate)
        {
            AppendAttr(sb, ExternalDefaults.HydrateAttribute, hydrate);
        }

        // AppendAttr, not sb.Append: it registers the Attribute frame as well as writing the markup.
        // Without the frame the value renders once and never diffs again, so a prop change would stop
        // reaching the adapter after the first paint.
        AppendAttr(sb, ExternalDefaults.PropsAttribute, SerializeProps());
    }

    /// <summary>The props object, as the JSON text the <c>props</c> attribute carries.</summary>
    private string SerializeProps()
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteObject(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // ----- children -----------------------------------------------------------------------------------------------

    // The props keys an island's children travel under. `$`-prefixed like the handler and date markers, which no
    // extracted prop may use, so a component's own prop can never collide with them.
    private const string ChildrenKey = "$c";
    private const string ChildNameKey = "n";
    private const string ChildKeyKey = "k";
    private const string ChildManifestKey = "m";
    private const string ChildPropsKey = "p";

    // Each element is an island of this island's runtime or the text a child renders as. Set during the enclosing
    // component's Render(), like Component.Children, and a lazy sequence is materialised then: a child island built by
    // the chain is the same entry on every render, so its handler ids do not churn.
    private object?[]? _islandChildren;

    // True while this island's props are being written. An island reached again inside its own children is a cycle,
    // which would otherwise recurse until the stack overflowed.
    private bool _writing;

    /// <summary>Refused: an island renders children of its own runtime only. Reported as RASK062.</summary>
    /// <remarks>
    ///     Hides <see cref="Component" />'s indexer rather than overriding it — an override cannot narrow the parameter,
    ///     so it could only throw while rendering. The result cannot become a component, so a wrong child is a compile
    ///     error; the island's own typed indexers, generated for every island whose component takes content, are the
    ///     ones its children bind to.
    /// </remarks>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public new NotAChildOfThisIsland this[params Component?[] children] => default;

    /// <summary>Refused: an island renders children of its own runtime only. Reported as RASK062.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public new NotAChildOfThisIsland this[IEnumerable<Component?> children] => default;

    /// <summary>Refused: an island renders children of its own runtime only. Reported as RASK062.</summary>
    public new NotAChildOfThisIsland this[params object?[] children] => default;

    /// <summary>
    ///     Nothing: an island's children travel inside its props and its framework renders them, so the host element
    ///     stays empty on the server.
    /// </summary>
    /// <remarks>
    ///     Sealed so no island can put Rask children below the opaque boundary, where the diff would never reach them
    ///     again. <see cref="Component.Children" /> assigned by hand is reported as RASK062 and lands here unrendered.
    /// </remarks>
    protected sealed override IEnumerable<Component?> RenderChildren() => [];

    /// <summary>Stores text children. Called by the generated <c>this[IEnumerable&lt;string?&gt;]</c> indexer.</summary>
    /// <param name="children">The text, one child per element; a null element renders nothing.</param>
    protected void SetTextChildren(IEnumerable<string?> children) => SetIslandChildren(children, static text => text);

    /// <summary>Stores children, materialised now, as the island or the text each one renders as.</summary>
    private protected void SetIslandChildren<TChild>(IEnumerable<TChild> children, Func<TChild, object?> value)
    {
        if (children is TChild[] array)
        {
            var values = new object?[array.Length];
            for (var i = 0; i < array.Length; i++)
            {
                values[i] = value(array[i]);
            }

            _islandChildren = values;
            return;
        }

        var list = new List<object?>();
        foreach (var child in children)
        {
            list.Add(value(child));
        }

        _islandChildren = list.ToArray();
    }

    /// <summary>This island's props object, children included — for its own attribute and for a parent's children.</summary>
    private void WriteObject(Utf8JsonWriter writer)
    {
        if (_writing)
        {
            throw new InvalidOperationException(
                $"'{ComponentName}' is among its own children, so its props would never finish writing.");
        }

        _writing = true;
        try
        {
            writer.WriteStartObject();
            WriteProps(writer);
            WriteChildren(writer);
            writer.WriteEndObject();
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>
    ///     Writes <c>"$c": [ … ]</c>: text as a string, a child island as
    ///     <c>{ "n": name, "k"?: key, "m"?: manifest, "p": { its props, "$c"?: its children } }</c>. Absent when no
    ///     children indexer was called, so an island without children writes exactly what it always wrote.
    /// </summary>
    private void WriteChildren(Utf8JsonWriter writer)
    {
        if (_islandChildren is not { } children)
        {
            return;
        }

        writer.WriteStartArray(ChildrenKey);
        foreach (var child in children)
        {
            switch (child)
            {
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case ExternalComponent island:
                    island.WriteAsChild(writer);
                    break;
            }
        }

        writer.WriteEndArray();
    }

    private void WriteAsChild(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString(ChildNameKey, ComponentName);
        if (KeyString is { } key)
        {
            writer.WriteString(ChildKeyKey, key);
        }

        // Only when it is not the app's own, as on the host element.
        if (ManifestUrl is { Length: > 0 } manifest
            && !string.Equals(manifest, ExternalDefaults.DefaultManifestUrl, StringComparison.Ordinal))
        {
            writer.WriteString(ChildManifestKey, manifest);
        }

        writer.WritePropertyName(ChildPropsKey);
        WriteObject(writer);
        writer.WriteEndObject();
    }
}
