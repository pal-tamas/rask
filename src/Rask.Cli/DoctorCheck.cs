using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>One <c>rask doctor</c> check.</summary>
internal sealed record DoctorCheck(string Name, DoctorStatus Status, string Detail, string? Fix);
