namespace Rask.Core;

/// <summary>
///     Publishes which of a component's properties a builder chain <i>must</i> set — the RASK001 rule
///     (non-nullable, no member initializer) — for the benefit of assemblies that only ever see the
///     component through metadata.
///     <para>
///         A member initializer compiles into the constructor and leaves no symbol-level trace, and
///         <c>DeclaringSyntaxReferences</c> is empty for a metadata symbol, so from a referencing
///         compilation <c>string Title</c> and <c>string Title = ""</c> are the same symbol. Only the
///         language's own <c>required</c> modifier survives. RASK038 therefore cannot <i>derive</i>
///         requiredness for a referenced library's component; the compilation that owns the component
///         has to publish it, which is what the factory generator emits this for — one attribute per
///         component with such a property, alongside the assembly's <c>RaskEntries{Assembly}</c> class.
///     </para>
///     <para>
///         Publish, don't re-derive: the owning compilation already computed this and already reported
///         its diagnostics, so a second derivation from metadata would be a silently divergent copy.
///     </para>
///     <para>
///         <paramref name="component" /> is a <b>name</b>, not a <c>typeof</c>, deliberately: a
///         <c>System.Type</c> argument is stored in the attribute blob as an assembly-qualified name that
///         the trimmer resolves and marks, which would root every component of a referenced component
///         library in every trimmed app. The name is the fully-qualified type with no <c>global::</c>
///         prefix, type arguments stripped, and a <c>`n</c> arity suffix for a generic type
///         (<c>Rask.Bootstrap.BsIcon</c>, <c>Rask.Core.Select`2</c>).
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RaskRequiredPropertiesAttribute(string component, params string[] properties) : Attribute
{
    /// <summary>The component the properties belong to, as described above.</summary>
    public string Component { get; } = component;

    /// <summary>The names of the properties a builder chain for that component must set.</summary>
    public string[] Properties { get; } = properties ?? [];
}
