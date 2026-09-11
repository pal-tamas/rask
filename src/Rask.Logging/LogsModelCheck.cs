using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Logging;

/// <summary>The application-database log store's claim on the model, checked once at boot. See #1015.</summary>
/// <remarks>
/// A type of its own, like every battery's check, so <c>AddHostedService</c>'s <c>TryAddEnumerable</c> has something
/// to deduplicate on and a repeated <c>AddRaskLogging&lt;TContext&gt;</c> call registers it once.
/// </remarks>
internal sealed class LogsModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Logs";

    protected override Type Entity => typeof(LogEntry);

    protected override string MapCall => "AddRaskLogging";
}
