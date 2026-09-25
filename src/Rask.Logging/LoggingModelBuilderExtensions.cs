using Microsoft.EntityFrameworkCore;

namespace Rask.Logging;

/// <summary>Model-building helper for the log table.</summary>
public static class LoggingModelBuilderExtensions
{
    /// <summary>
    /// Maps the <c>RaskLog</c> table that <c>AddRaskLogging&lt;TContext&gt;()</c> writes to. Call from your
    /// context's <c>OnModelCreating</c>, then create the table with <c>rask db add AddLogs &amp;&amp; rask db update</c>.
    /// </summary>
    /// <remarks>
    /// Only for the application-database store. <c>AddRaskLogging()</c> keeps its log in a SQLite
    /// file of its own and needs nothing in your model.
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ModelBuilder AddRaskLogging(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new LogEntryConfiguration());
        return modelBuilder;
    }
}
