using Microsoft.EntityFrameworkCore;

namespace Rask.Background;

/// <summary>Model-building helper for the jobs tables.</summary>
public static class JobsModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="Job"/> and <see cref="RecurringJobState"/> tables. Call from your context's
    /// <c>OnModelCreating</c>, then create the schema with <c>rask db add AddJobs &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskJobs(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new JobConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringJobStateConfiguration());
        return modelBuilder;
    }
}
