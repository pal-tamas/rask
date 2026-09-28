using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>The <c>rask deploy status --json</c> document.</summary>
internal sealed record DeployStatusReport(string Host, IReadOnlyList<DeployedAppStatus> Apps);
