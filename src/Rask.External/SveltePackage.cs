namespace Rask.External;

/// <summary>
///     Several Svelte components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="SvelteComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Bits : SveltePackage
///     {
///         protected override string Module => "bits-ui";
///         protected override string[] Exports => ["Switch.Root", "Checkbox.Root"];
///     }
///     </code>
/// </example>
public abstract class SveltePackage : ExternalPackage;
