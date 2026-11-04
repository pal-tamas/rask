namespace Rask.Wasm.Fixture.Wasm.Islands;

/// <summary>
///     A Lit-runtime island — a plain custom element, bundled by the build — whose button goes to the desk with the
///     app's own typed routes (<c>Routes.DeskPage().Go()</c> from <c>@rask/routes</c>). Served under a sub-path, it
///     proves the runtime, the manifest and the chunk load under the base, and that its navigation stays there.
/// </summary>
public sealed partial class DeskLink : Rask.External.LitComponent
{
    /// <summary>The button's caption.</summary>
    public required string Label { get; set; }
}
