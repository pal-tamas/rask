using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>The <c>rask deploy status --json</c> document; <see cref="Disk"/> is absent when the box's disk could not be read.</summary>
internal sealed record DeployStatusReport(string Host, IReadOnlyList<DeployedAppStatus> Apps, DeployDiskStatus? Disk = null);
