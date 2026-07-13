namespace Rask.Core;

/// <summary>
///     Marks an assembly as contributing a generated factory namespace (its <c>{Namespace}.Generated</c>
///     static class) that consumers should see via a global <c>using static</c>. The Rask factory generator
///     scans referenced assemblies for this marker and, for each one present in the reference graph, emits
///     <c>global using static global::{Namespace}.Generated;</c> into the consuming compilation — so a native
///     app that references <c>Rask.Native</c> can call <c>NativeHeaderBar(...)</c> with no per-file using,
///     exactly like the core element factories.
///     <para>
///         The core framework namespaces (<c>Rask.Core.Components</c>, <c>Rask.Core.Routing</c>) are emitted
///         unconditionally and need no marker; this attribute is how satellite factory families
///         (<c>Rask.Native.Components</c>, and any future family) opt in without the generator hardcoding them.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RaskFactoryNamespaceAttribute(string ns) : Attribute
{
    /// <summary>The factory namespace whose <c>.Generated</c> static class to surface globally.</summary>
    public string Namespace { get; } = ns;
}
