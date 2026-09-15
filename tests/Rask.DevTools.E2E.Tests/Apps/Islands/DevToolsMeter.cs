namespace Rask.DevTools.E2E.Tests.Apps.Islands;

/// <summary>
///     A Lit-runtime island — a plain custom element, bundled by the build — that the devtools must show as a component
///     row badged <c>Lit</c> with these props, and whose failure they must list.
/// </summary>
public sealed partial class DevToolsMeter : Rask.External.LitComponent
{
    /// <summary>The caption.</summary>
    public required string Label { get; set; }

    /// <summary>The reading.</summary>
    public int Value { get; set; } = 0;

    /// <summary>When true, the element's setter throws as the island mounts: the failure the devtools list.</summary>
    public bool Broken { get; set; } = false;
}
