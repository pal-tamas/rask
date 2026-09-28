using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>The <c>rask db list --json</c> document.</summary>
internal sealed record MigrationListReport(IReadOnlyList<MigrationEntry> Migrations);
