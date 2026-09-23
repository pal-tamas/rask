namespace Rask.External;

/// <summary>
///     Several Solid components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="SolidComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Kobalte : SolidPackage
///     {
///         protected override string Module => "@kobalte/core";
///         protected override string[] Exports => ["Button", "Switch.Root"];
///     }
///     </code>
/// </example>
public abstract class SolidPackage : ExternalPackage;
