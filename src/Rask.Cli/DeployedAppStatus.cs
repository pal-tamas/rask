using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>
///     One row of <c>rask deploy status --json</c>. Named for the report rather than the thing, because
///     <c>DeployedApp</c> already exists for the Caddy routing path and means something narrower.
/// </summary>
internal sealed record DeployedAppStatus(
    string App,
    string Container,
    string? Domain,
    string? Ports,
    string? Color,
    string Status,
    bool IsCurrentProject);
