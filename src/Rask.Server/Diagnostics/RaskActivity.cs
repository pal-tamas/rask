using System.Diagnostics;

namespace Rask.Server.Diagnostics;

/// <summary>
///     The Rask server's <see cref="ActivitySource" /> (named <see cref="RaskTelemetry.ActivitySourceName" />).
///     A long-lived singleton, as the <c>ActivitySource</c> docs require. Subscribe from an
///     OpenTelemetry tracer with <c>.AddSource(RaskTelemetry.ActivitySourceName)</c>. With no
///     listener registered, <c>ActivitySource.StartActivity</c> returns <c>null</c>
///     and the instrumentation costs nothing.
/// </summary>
internal static class RaskActivity
{
    public static readonly ActivitySource Source = new(RaskTelemetry.ActivitySourceName);
}
