using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     The services a <see cref="TestDatabase" /> saves through, registered as an app registers them — so a save in a test
///     runs the same interceptors in the same order, and publishes its domain events to the same handlers.
/// </summary>
/// <remarks>
///     The dispatcher and every generated handler (<c>AddRaskCqrs</c>), Rask.Data's interceptors (<c>AddRaskData</c>), and
///     what a battery the model maps adds to them — the outbox, once the model has its table. Its contexts are the test
///     database's own, through <see cref="IDbContextFactory{TContext}" />, so a handler that opens one writes beside the test.
/// </remarks>
internal static class TestDatabaseServices
{
    /// <summary>The services, with the batteries <paramref name="model" /> maps — none while it is not known yet.</summary>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    public static ServiceProvider For<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        TimeProvider clock,
        IModel? model,
        Func<TContext> openWrite,
        Func<RaskReadDbContext> openRead,
        Action<IServiceCollection>? addServices)
        where TContext : RaskDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Before AddRaskData, whose TryAdd would otherwise register Rask's clock in place of the test's own.
        services.AddSingleton(clock);

        services.AddSingleton<IDbContextFactory<TContext>>(new TestContextFactory<TContext>(openWrite));
        services.AddScoped(static sp => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext());
        services.AddSingleton<IDbContextFactory<RaskReadDbContext>>(new TestContextFactory<RaskReadDbContext>(openRead));

        // As the host registers them: the dispatcher first, then Data, then the batteries that follow it.
        services.AddRaskCqrs();
        services.AddRaskData<TContext>();
        if (model is not null)
        {
            foreach (var battery in TestDatabaseBatteries.All)
            {
                battery.Wire<TContext>(services, model);
            }
        }

        // The test's own last, so it can stand in for any of the above.
        addServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>The interceptors <paramref name="services" /> hold, in the order the app's context is handed them.</summary>
    public static IInterceptor[] Interceptors(IServiceProvider services) => [.. services.GetServices<ISaveChangesInterceptor>()];
}
