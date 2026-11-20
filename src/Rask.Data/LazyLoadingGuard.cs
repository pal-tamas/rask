using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data;

/// <summary>
///     Switches lazy loading off on every context the Rask interceptors reach, unless its options said
///     <see cref="LazyLoadingOptionsBuilderExtensions.AllowLazyLoading(DbContextOptionsBuilder)" />.
/// </summary>
/// <remarks>
///     EF Core has no "context created" hook, so this takes the first moments a context can load anything: the first
///     command it creates (every query, every save) and every entity it materializes — where proxies and the injected
///     <c>ILazyLoader</c> come from. Being a save interceptor is what puts it on every context, as for
///     <see cref="AmbientTransactionInterceptor" />; EF Core takes each role an interceptor implements.
/// </remarks>
internal sealed class LazyLoadingGuard : DbCommandInterceptor, IMaterializationInterceptor, ISaveChangesInterceptor
{
    internal static readonly LazyLoadingGuard Instance = new();

    private LazyLoadingGuard()
    {
    }

    public override InterceptionResult<DbCommand> CommandCreating(
        CommandCorrelatedEventData eventData, InterceptionResult<DbCommand> result)
    {
        SwitchOff(eventData.Context);
        return result;
    }

    InterceptionResult<object> IMaterializationInterceptor.CreatingInstance(
        MaterializationInterceptionData materializationData, InterceptionResult<object> result)
    {
        SwitchOff(materializationData.Context);
        return result;
    }

    InterceptionResult<int> ISaveChangesInterceptor.SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SwitchOff(eventData.Context);
        return result;
    }

    ValueTask<InterceptionResult<int>> ISaveChangesInterceptor.SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
    {
        SwitchOff(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>Whether <paramref name="options" /> said AllowLazyLoading().</summary>
    internal static bool Allows(IDbContextOptions options) =>
        options.FindExtension<CoreOptionsExtension>()?.Interceptors?.Contains(LazyLoadingAllowed.Instance) == true;

    private static void SwitchOff(DbContext? context)
    {
        try
        {
            if (context is { ChangeTracker.LazyLoadingEnabled: true } && !Allows(context.GetService<IDbContextOptions>()))
            {
                context.ChangeTracker.LazyLoadingEnabled = false;
            }
        }
        catch (ObjectDisposedException)
        {
            // An object built from the values of a context already gone (PropertyValues.ToObject): it loads nothing.
        }
    }
}
