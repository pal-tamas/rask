using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Rask.Data;

/// <summary>
///     Tells the save interceptors after one in a context's chain that the save failed, which EF Core does not once that
///     one throws: a refusal before the save (<see cref="AuditingInterceptor" />) or a translation in the database's
///     place (<see cref="ViolationMessageInterceptor" />).
/// </summary>
/// <remarks>
///     A test database puts the keeper of its own context there (<see cref="FixtureContextKeeper" />): told nothing, it
///     would leave a refused change tracked, to be refused again on the next save.
/// </remarks>
internal static class LaterSaveInterceptors
{
    /// <summary>What a save that <paramref name="saving" /> started reports when it fails with <paramref name="failure" />.</summary>
    public static DbContextErrorEventData Failed(DbContextEventData saving, Exception failure)
    {
        var context = saving.Context!;
        return new DbContextErrorEventData(
            new SaveFailed(context.GetService<ILoggingOptions>()),
            static (_, data) => ((DbContextErrorEventData)data).Exception.Message,
            context,
            failure);
    }

    /// <summary>Tells each interceptor after <paramref name="self" /> that the save <paramref name="failed" /> reports failed.</summary>
    public static void TellFailed(IInterceptor self, DbContextErrorEventData failed)
    {
        foreach (var later in After(self, failed.Context))
        {
            later.SaveChangesFailed(failed);
        }
    }

    /// <inheritdoc cref="TellFailed(IInterceptor, DbContextErrorEventData)" />
    public static async Task TellFailedAsync(IInterceptor self, DbContextErrorEventData failed, CancellationToken cancellationToken)
    {
        foreach (var later in After(self, failed.Context))
        {
            await later.SaveChangesFailedAsync(failed, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ISaveChangesInterceptor[] After(IInterceptor self, DbContext? context)
    {
        var chain = context?.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors;
        if (chain is null)
        {
            return [];
        }

        return [.. chain.SkipWhile(i => !ReferenceEquals(i, self)).Skip(1).OfType<ISaveChangesInterceptor>()];
    }

    // EF Core's own event for a failed save, which it does not raise for a save an interceptor refused before it began.
    private sealed class SaveFailed(ILoggingOptions options)
        : EventDefinitionBase(options, CoreEventId.SaveChangesFailed, LogLevel.Error, "CoreEventId.SaveChangesFailed");
}
