using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Jobs;

/// <summary>The job queue's claim on the application's model, checked once at boot. See #1015.</summary>
internal sealed class JobsModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Jobs";

    protected override Type Entity => typeof(Job);

    protected override string MapCall => "AddRaskJobs";
}
