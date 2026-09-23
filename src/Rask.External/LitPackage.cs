namespace Rask.External;

/// <summary>
///     Several Lit components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="LitComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Shoelace : LitPackage
///     {
///         protected override string Module => "@shoelace-style/shoelace/dist/shoelace.js";
///         protected override string[] Exports => ["sl-button", "sl-switch"];
///     }
///     </code>
/// </example>
public abstract class LitPackage : ExternalPackage;
