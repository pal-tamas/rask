using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Hands every entity a query materializes, and every entity a save is about to write, collections of children
///     EF Core can fill — see <see cref="WritableChildren" />.
/// </summary>
/// <remarks>
///     Registered first by <see cref="RaskDataServiceCollectionExtensions.AddRaskData(Microsoft.Extensions.DependencyInjection.IServiceCollection)" />,
///     so a refill the other interceptors match is already writable, and added by <see cref="RaskDbContext" /> to a
///     context configured without it.
/// </remarks>
internal sealed class WritableChildrenInterceptor : SaveChangesInterceptor, IMaterializationInterceptor
{
    /// <summary>The one instance: a materialization interceptor is part of EF Core's service-provider cache key.</summary>
    internal static readonly WritableChildrenInterceptor Instance = new();

    private WritableChildrenInterceptor()
    {
    }

    /// <inheritdoc />
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        WritableChildren.Ensure(materializationData.EntityType, entity);
        return entity;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Ensure(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Ensure(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Ensure(DbContext? context)
    {
        if (context is not null)
        {
            WritableChildren.Ensure(context);
        }
    }
}
