namespace Rask.External;

/// <summary>
///     Several Preact components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="PreactComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Ui : PreactPackage
///     {
///         protected override string Module => "preact-ui-kit";
///         protected override string[] Exports => ["Button", "Card"];
///     }
///     </code>
/// </example>
public abstract class PreactPackage : ExternalPackage;
