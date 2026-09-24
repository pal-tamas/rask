namespace Rask.External;

/// <summary>
///     Several Vue components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="VueComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Prime : VuePackage
///     {
///         protected override string Module => "primevue";
///         protected override string[] Exports => ["Button", "Card"];
///     }
///     </code>
/// </example>
public abstract class VuePackage : ExternalPackage;
