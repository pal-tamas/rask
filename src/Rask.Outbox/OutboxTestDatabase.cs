using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Outbox;

/// <summary>
///     The outbox in a <c>TestDatabase</c>, as in the app: a model that maps its table gets its interceptor, so a durable
///     handler's event is stored in the save's own transaction (or recorded by <c>Outbox.Fake()</c>).
/// </summary>
internal sealed class OutboxTestDatabase : ITestDatabaseBattery
{
    /// <summary>Runs when this assembly is first used — by a model that maps the outbox, before its test database wires.</summary>
    // CA2255 warns that a module initializer in a library surprises its consumer. Here it is the mechanism: Rask.Data's
    // TestDatabase must wire the outbox as the host does, and cannot reference this assembly to ask. The initializer adds
    // one object to a list, and only a TestDatabase reads it.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => TestDatabaseBatteries.Add(new OutboxTestDatabase());

    /// <inheritdoc />
    public void Wire<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(IServiceCollection services, IModel model)
        where TContext : DbContext
    {
        // An app always has the outbox, and its boot refuses a model without the table (OutboxModelCheck); a test
        // database over such a model simply has no outbox, so a durable handler runs in memory as Rask.Cqrs alone runs it.
        if (model.FindEntityType(typeof(OutboxMessage)) is not null)
        {
            services.AddRaskOutbox<TContext>();
        }
    }
}
