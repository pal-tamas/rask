using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.WebPush;

/// <summary>
///     Web Push in a <c>TestDatabase</c>: its fake, so a save handler's <c>Push.Send</c> is recorded rather than thrown
///     for want of a host — <c>database.Push.Sent().Once()</c>.
/// </summary>
public static class PushTestDatabase
{
    extension(TestDatabase database)
    {
        /// <summary>
        ///     The push fake this database stands in for the battery with, once its model maps the push subscriber table (a
        ///     RaskApp's context does): every push the test's code sent, to ask about.
        /// </summary>
        /// <exception cref="InvalidOperationException">The model maps no push table, or the test registered its own.</exception>
        public PushFake Push =>
            database.Fakes.Required<PushFake>("push", "modelBuilder.AddRaskWebPush()", "Push.Fake()");
    }

    /// <summary>Runs when this assembly is first used — by a model that maps the push table, before its test database wires.</summary>
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
            if (model.FindEntityType(typeof(PushSubscriber)) is not null)
            {
                fakes.Use<IPush, PushFake>(services, global::Rask.WebPush.Push.Faked.Value as PushFake, static () => new PushFake());
            }
        }
    }
}
