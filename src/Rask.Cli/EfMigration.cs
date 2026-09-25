using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>
///     One entry of <c>dotnet ef migrations list --json</c>, which is what <c>rask db list --json</c> is
///     built from rather than from the human listing.
/// </summary>
/// <remarks>
///     EF's shape, not ours, and deliberately kept separate from <see cref="MigrationEntry" /> so their
///     fields can diverge without one silently reshaping the other. <c>safeName</c> is EF's
///     identifier-safe variant and is dropped — it exists for code generation, not for a report.
/// </remarks>
internal sealed record EfMigration(string Id, string Name, string? SafeName, bool Applied);
