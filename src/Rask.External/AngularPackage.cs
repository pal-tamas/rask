namespace Rask.External;

/// <summary>
///     Several Angular components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="AngularComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Mat : AngularPackage
///     {
///         protected override string Module => "@acme/angular-ui";
///         protected override string[] Exports => ["AcButton", "AcCard"];
///     }
///     </code>
/// </example>
public abstract class AngularPackage : ExternalPackage;
