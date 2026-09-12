namespace Rask.Site.Features.Islands;

/// <summary>
///     react-colorful's <c>HexColorPicker</c>, used straight from npm — no <c>.tsx</c> beside it.
/// </summary>
/// <remarks>
///     <para>
///         A <b>package island</b>: <see cref="Module" /> names a package export instead of a file, and the chain steps
///         — <c>.Color(…)</c>, <c>.OnChange(…)</c>, <c>.OnChangeEnd(…)</c> — are generated from
///         <c>ColorPicker.props.json</c> beside this class, which the build extracts from the package's own TypeScript
///         declarations. That snapshot is committed, so a fresh clone, the IDE and a build without Node all see the same
///         steps; bumping react-colorful rewrites it and the diff shows what changed.
///     </para>
///     <para>
///         The component takes no content, so this island takes no children. It is one: <see cref="IslandsDemo" />
///         nests it inside <see cref="ReactCounter" />, and React renders both in one tree.
///     </para>
/// </remarks>
public sealed partial class ColorPicker : Rask.External.ReactComponent
{
    /// <summary>The package and the export this island renders.</summary>
    protected override string Module => "react-colorful#HexColorPicker";
}
