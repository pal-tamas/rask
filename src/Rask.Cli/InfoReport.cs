using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>The <c>rask info --json</c> document.</summary>
internal sealed record InfoReport(
    [property: JsonPropertyName("raskCli")] string RaskCli,
    [property: JsonPropertyName("dotnetSdk")] string? DotnetSdk,
    [property: JsonPropertyName("os")] string Os);
