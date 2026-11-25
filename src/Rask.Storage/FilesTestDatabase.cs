using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Storage;

/// <summary>
///     File storage in a <c>TestDatabase</c>: its fake, so a save handler's <c>Files.Save</c> stores in memory rather
///     than throwing for want of a host — <c>database.Files.Saved().Named("avatar.png").Once()</c>.
/// </summary>
public static class FilesTestDatabase
{
    extension(TestDatabase database)
    {
        /// <summary>
        ///     The file-storage fake this database stands in for the battery with, once its model maps the stored-file table
        ///     (a RaskApp's context does): an in-memory store that really keeps what is saved, to ask about.
        /// </summary>
        /// <exception cref="InvalidOperationException">The model maps no stored-file table, or the test registered its own.</exception>
        public FilesFake Files =>
            database.Fakes.Required<FilesFake>("file storage", "modelBuilder.AddRaskStorage()", "Files.Fake()");
    }

    /// <summary>Runs when this assembly is first used — by a model that maps the stored-file table, before its test database wires.</summary>
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
            if (model.FindEntityType(typeof(StoredFile)) is not null)
            {
                fakes.Use<IFiles, FilesFake>(services, global::Rask.Storage.Files.Faked.Value as FilesFake, static () => new FilesFake());
            }
        }
    }
}
