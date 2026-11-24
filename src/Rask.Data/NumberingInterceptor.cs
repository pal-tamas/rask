using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Keeps every declared numbering (<see cref="NumberedBuilderExtensions" />) as a save goes out: notes the groups it
///     touches and locks them before its rows are written, and numbers them anew once they were, in its transaction
///     (<see cref="Numberings" />). Registered after the stamps, so it sees the rows as they go out.
/// </summary>
internal sealed class NumberingInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Numberings.BeforeSave(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            await Numberings.BeforeSave(context, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
