namespace Rask.Server.E2E.Tests.Islands;

/// <summary>
///     A Lit-runtime island — a plain custom element, bundled by this project's build — whose button navigates with
///     the app's typed routes from <c>@rask/routes</c>, so the Server host's <c>__raskHost.navigate</c> bridge is what
///     carries it.
/// </summary>
public sealed partial class IslandGo : Rask.External.LitComponent
{
    /// <summary>What the element shows once it has mounted.</summary>
    public required string Caption { get; set; }
}
