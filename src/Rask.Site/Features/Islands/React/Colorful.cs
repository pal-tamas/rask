namespace Rask.Site.Features.Islands;

/// <summary>
///     react-colorful, used straight from npm — no <c>.tsx</c> beside it. Its colour picker and its hex field are
///     <c>Colorful.HexColorPicker</c> and <c>Colorful.HexColorInput</c>.
/// </summary>
/// <remarks>
///     <para>
///         A <b>package declaration</b>: <see cref="Module" /> names the package and <see cref="Exports" /> the
///         components wanted from it, and each export becomes a package island of its own. Its chain steps —
///         <c>.Color(…)</c>, <c>.OnChange(…)</c> — are generated from <c>ColorfulHexColorPicker.props.json</c> and
///         <c>ColorfulHexColorInput.props.json</c> beside this class, which the build extracts from the package's own
///         TypeScript declarations. The snapshots are committed, so a fresh clone, the IDE and a build without Node all
///         see the same steps; bumping react-colorful rewrites them and the diff shows what changed.
///     </para>
///     <para>
///         Each component is reached through this class rather than by a bare name of its own, so the page reads by
///         package and nothing it exports can shadow an HTML tag. Neither takes content, so neither takes children;
///         both are children themselves — <see cref="IslandsDemo" /> nests them inside <see cref="ReactCounter" />, and
///         React renders all three in one tree.
///     </para>
/// </remarks>
public sealed partial class Colorful : Rask.External.ReactPackage
{
    /// <summary>The package the components are imported from.</summary>
    protected override string Module => "react-colorful";

    /// <summary>The components this app uses from it.</summary>
    protected override string[] Exports => ["HexColorPicker", "HexColorInput"];
}
