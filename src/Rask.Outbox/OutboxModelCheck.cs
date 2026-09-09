using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Outbox;

/// <summary>The outbox's claim on the application's model, checked once at boot. See #1015.</summary>
/// <remarks>
/// The outbox fails the most quietly of the batteries when it is unmapped: its interceptor writes the
/// event in the same transaction as the change that raised it, so an unmapped table does not merely
/// delay delivery — the write that was supposed to be atomic with the domain change cannot happen at
/// all.
/// </remarks>
internal sealed class OutboxModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Outbox";

    protected override Type Entity => typeof(OutboxMessage);

    protected override string MapCall => "AddRaskOutbox";
}
