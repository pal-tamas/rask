namespace Rask.Server.Diagnostics;

/// <summary>
///     Well-known telemetry names for the Rask server host. Use these to subscribe from an
///     OpenTelemetry pipeline (<c>builder.AddMeter(RaskTelemetry.MeterName)</c> /
///     <c>.AddSource(RaskTelemetry.ActivitySourceName)</c>) or <c>dotnet-counters</c>
///     (<c>--counters Rask.Server</c>).
/// </summary>
public static class RaskTelemetry
{
    /// <summary>The <see cref="System.Diagnostics.Metrics.Meter" /> name for all Rask server metrics.</summary>
    public const string MeterName = "Rask.Server";

    /// <summary>The <see cref="System.Diagnostics.ActivitySource" /> name for Rask server traces.</summary>
    public const string ActivitySourceName = "Rask.Server";
}
