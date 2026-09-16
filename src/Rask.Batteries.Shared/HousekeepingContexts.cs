using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Batteries;

/// <summary>
///     An <see cref="IDbContextFactory{TContext}" /> for a battery's OWN bookkeeping — the claim, the
///     sweep, the purge — whose SQL is logged at <see cref="LogLevel.Debug" /> rather than the
///     <see cref="LogLevel.Information" /> EF Core logs every command at.
/// </summary>
/// <remarks>
///     <para>
///         EF Core logs every statement it executes at <c>Information</c>, under the category
///         <c>Microsoft.EntityFrameworkCore.Database.Command</c>, and a battery polls on a timer — five
///         seconds by default for jobs, mail and the outbox. An app with those three on, doing
///         <em>nothing</em>, therefore writes a six-line SQL block to the console roughly every 1.7
///         seconds, for as long as it runs:
///     </para>
///     <code>
///     info: Microsoft.EntityFrameworkCore.Database.Command[20101]
///           Executed DbCommand (0ms) [Parameters=[…]], CommandType='Text', CommandTimeout='30'
///           SELECT "j"."Id"
///           FROM "Job" AS "j"
///           WHERE "j"."ProcessedAt" IS NULL AND …
///     </code>
///     <para>
///         That is the whole console, and none of it is the application's. The obvious lever —
///         <c>"Microsoft.EntityFrameworkCore.Database.Command": "Warning"</c> in <c>appsettings.json</c>
///         — costs too much, because it also hides the queries the developer actually wants to read:
///         their own. So the level is lowered for the housekeeping <em>context</em> instead, leaving
///         every context the application creates exactly as loud as it was. A developer who does want
///         to watch a battery claim its batch turns the category up to <c>Debug</c> and sees it again.
///     </para>
///     <para>
///         The event id is written out rather than taken from <c>RelationalEventId.CommandExecuted</c>,
///         because that constant lives in <c>Microsoft.EntityFrameworkCore.Relational</c> and the
///         batteries carry no package they do not need — <c>Rask.Cache</c> has no Rask reference at all
///         (the dependency creep #1014 is open about, and the same reason this file is source-linked
///         rather than living in an assembly of its own). EF matches a configured warning by
///         <see cref="EventId.Id" />, so the number is what has to be right; the name is here to read.
///     </para>
///     <para>
///         <b>The application's own factory stays in charge whenever this cannot copy its registration
///         exactly.</b> The options are rebuilt from the <see cref="DbContextOptions{TContext}" /> the
///         app registered, which means this only applies to EF's own factory: a hand-written
///         <see cref="IDbContextFactory{TContext}" /> exists to <em>do</em> something — pick a tenant's
///         connection, stamp a filter — and copying its options would quietly drop that. A context with
///         no constructor this can call, a scoped dependency, no registered options at all: every one of
///         them falls back to the app's factory and a noisy log. Noisy but correct beats quiet but wrong.
///     </para>
///     <para>
///         Resolution is lazy because a scaffolded <c>Program.cs</c> calls
///         <c>AddRaskOutbox&lt;AppDbContext&gt;()</c> <em>before</em>
///         <c>AddDbContextFactory&lt;AppDbContext&gt;(…)</c>; reading the options at registration would
///         see nothing and give up on every app Rask itself scaffolds.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The application <see cref="DbContext" /> the battery was wired against.</typeparam>
internal sealed class HousekeepingContextFactory<TContext>(IServiceProvider services) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    /// <summary><c>RelationalEventId.CommandExecuted</c>, written out — see the remarks.</summary>
    private static readonly EventId CommandExecuted =
        new(20101, DbLoggerCategory.Database.Command.Name + ".CommandExecuted");

    private readonly object _gate = new();

    private DbContextOptions<TContext>? _quiet;
    private bool _resolved;

    /// <inheritdoc />
    public TContext CreateDbContext()
    {
        if (QuietOptions() is { } quiet)
        {
            try
            {
                return (TContext)ActivatorUtilities.CreateInstance(services, typeof(TContext), quiet);
            }
#pragma warning disable CA1031 // A quieter log is never worth failing the sweep over: fall back to the app's factory.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Once, and for the life of the app: whatever shape this context is, it is not one that
                // can be built from options alone, and retrying every poll would cost more than the log.
                lock (_gate)
                {
                    _quiet = null;
                }
            }
        }

        return services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext();
    }

    private DbContextOptions<TContext>? QuietOptions()
    {
        lock (_gate)
        {
            if (_resolved)
            {
                return _quiet;
            }

            _resolved = true;
            _quiet = Build();
            return _quiet;
        }
    }

    private DbContextOptions<TContext>? Build()
    {
        // EF's own factory, or none of this applies — see the remarks.
        if (services.GetService<IDbContextFactory<TContext>>() is not { } factory
            || factory.GetType().FullName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) != true)
        {
            return null;
        }

        if (services.GetService<DbContextOptions<TContext>>() is not { } options)
        {
            return null;
        }

        return new DbContextOptionsBuilder<TContext>(options)
            .ConfigureWarnings(w => w.Log((CommandExecuted, LogLevel.Debug)))
            .Options;
    }
}

/// <summary>Registers a battery's background service against a <see cref="HousekeepingContextFactory{TContext}" />.</summary>
internal static class HousekeepingServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="TService" /> as a hosted service whose
    ///     <see cref="IDbContextFactory{TContext}" /> is the quiet one.
    /// </summary>
    /// <remarks>
    ///     <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)" />
    ///     is what makes a repeated <c>AddRaskX</c> register one service rather than two, and it
    ///     deduplicates on the descriptor's implementation type. A factory descriptor has none to read
    ///     directly, so it reports the factory delegate's own return type instead — which is why this
    ///     builds the descriptor through the two-argument generic overload, typing the delegate
    ///     <c>Func&lt;IServiceProvider, TService&gt;</c> rather than <c>Func&lt;IServiceProvider, object&gt;</c>.
    ///     Registering twice is pinned by each battery's own idempotency test.
    /// </remarks>
    /// <typeparam name="TService">The battery's background service.</typeparam>
    /// <typeparam name="TContext">The application <see cref="DbContext" /> that owns its tables.</typeparam>
    public static IServiceCollection AddHousekeepingService<TService, TContext>(this IServiceCollection services)
        where TService : class, IHostedService
        where TContext : DbContext
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TService>(
            static sp => ActivatorUtilities.CreateInstance<TService>(sp, new HousekeepingContextFactory<TContext>(sp))));

        return services;
    }
}
