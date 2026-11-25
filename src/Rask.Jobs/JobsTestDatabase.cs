using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background;

/// <summary>
///     The job queue in a <c>TestDatabase</c>: its fake, so a save handler's <c>Jobs.Enqueue</c> is recorded rather than
///     thrown for want of a host — <c>database.Jobs.Enqueued&lt;SendReceipt&gt;().Once()</c>, then
///     <c>await database.Jobs.Run()</c> runs it through its real handler, in the database's own services.
/// </summary>
public static class JobsTestDatabase
{
    extension(TestDatabase database)
    {
        /// <summary>
        ///     The job-queue fake this database stands in for the battery with, once its model maps the job table (a
        ///     RaskApp's context does): every job the test's code enqueued, to ask about and to run.
        /// </summary>
        /// <exception cref="InvalidOperationException">The model maps no job table, or the test registered its own.</exception>
        public JobsFake Jobs =>
            database.Fakes.Required<JobsFake>("job queue", "modelBuilder.AddRaskJobs()", "Jobs.Fake()");
    }

    /// <summary>Runs when this assembly is first used — by a model that maps the job table, before its test database wires.</summary>
    // CA2255 warns that a module initializer in a library surprises its consumer. Here it is the mechanism, as for the
    // outbox's (OutboxTestDatabase): Rask.Data cannot reference this assembly to ask for its fake.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => TestDatabaseBatteries.Add(new Battery());

    private sealed class Battery : ITestDatabaseBattery
    {
        public void Wire<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
            IServiceCollection services, IModel model, TestDatabaseFakes fakes)
            where TContext : DbContext
        {
            if (model.FindEntityType(typeof(Job)) is not null)
            {
                fakes.Use<IJobs, JobsFake>(services, global::Rask.Background.Jobs.Faked.Value as JobsFake, static () => new JobsFake());
            }
        }
    }
}
