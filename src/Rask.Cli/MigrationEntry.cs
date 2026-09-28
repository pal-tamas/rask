using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>One migration in <c>rask db list --json</c>.</summary>
internal sealed record MigrationEntry(string Id, string Name, bool Applied);
