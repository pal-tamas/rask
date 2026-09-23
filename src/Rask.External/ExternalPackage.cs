using System.Diagnostics.CodeAnalysis;

namespace Rask.External;

/// <summary>
///     Several components from one npm package, each reached through this class: <c>Mui.Button</c>, <c>Mui.Card</c>.
/// </summary>
/// <remarks>
///     <para>
///         Derive from the runtime's package class — <see cref="ReactPackage" />, <see cref="VuePackage" />, … — rather
///         than from this directly: the base class names the runtime, as it does for a single island.
///     </para>
///     <code>
///     public sealed partial class Mui : ReactPackage
///     {
///         protected override string Module => "@mui/material";
///         protected override string[] Exports => ["Button", "Card", "TextField"];
///     }
///
///     // markup
///     Mui.Card[ Mui.Button.Variant(MuiButtonVariant.Contained).OnClick(Save)["Save"] ]
///     </code>
///     <para>
///         Each export becomes a package island of its own, named after this class and the export — <c>MuiButton</c> —
///         with its props generated from <c>MuiButton.props.json</c> beside this file, exactly as a single
///         <c>Module</c>/<c>Export</c> island's are. Its chain entry is a member of this class, so it never takes a
///         bare name: <c>Button</c> stays the HTML <c>&lt;button&gt;</c>.
///     </para>
///     <para>
///         <c>partial</c> is required, because the entries are generated into it; <see cref="Module" /> and
///         <see cref="Exports" /> must be constants, because the build reads them before anything compiles.
///     </para>
/// </remarks>
public abstract class ExternalPackage
{
    /// <summary>Derive from a runtime's package class instead; one derived from this directly names no runtime.</summary>
    protected ExternalPackage()
    {
    }

    /// <summary>The npm package the exports are imported from, as the browser imports it.</summary>
    protected abstract string Module { get; }

    /// <summary>
    ///     The named exports of <see cref="Module" /> to use, each becoming one component of this package. A dotted
    ///     name reaches a member of an export (<c>"Switch.Root"</c> is <c>SwitchRoot</c>), and a Lit package names the
    ///     tags its module registers (<c>"sl-switch"</c> is <c>SlSwitch</c>).
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "Never called: the build reads the literal out of the syntax. An array is what a collection "
                        + "expression reads as, so the override stays one short line.")]
    protected abstract string[] Exports { get; }
}
