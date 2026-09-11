namespace Rask.External.Tests;

/// <summary>
///     MUI's Button, used directly: no front-end file beside it, and every prop generated from the committed
///     <c>MuiButton.props.json</c> — the snapshot the build extracts from the package's own TypeScript.
/// </summary>
/// <remarks>
///     The snapshot here is a fixture written by hand in the extractor's format, so this project needs no npm
///     install to exercise the generated half. What is asserted is everything downstream of the snapshot: the
///     chain steps, the props JSON, and a callback's argument crossing back into C#.
/// </remarks>
public sealed partial class MuiButton : ReactComponent
{
    /// <summary>The package's own module — which is what makes this a package island.</summary>
    protected override string Module => "@mui/material/Button";
}
