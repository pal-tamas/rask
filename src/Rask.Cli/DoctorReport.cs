using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>The <c>rask doctor --json</c> document.</summary>
internal sealed record DoctorReport(bool Ok, IReadOnlyList<DoctorCheck> Checks);
