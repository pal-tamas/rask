namespace Rask.External;

/// <summary>
///     Several React components from one npm package, each reached through the class that derives from this.
/// </summary>
/// <remarks>See <see cref="ExternalPackage" />; each export is a <see cref="ReactComponent" /> of its own.</remarks>
/// <example>
///     <code>
///     public sealed partial class Mui : ReactPackage
///     {
///         protected override string Module => "@mui/material";
///         protected override string[] Exports => ["Button", "Card"];
///     }
///     </code>
/// </example>
public abstract class ReactPackage : ExternalPackage;
