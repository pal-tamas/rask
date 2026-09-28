using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.WebPush;

internal sealed class PushModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Web Push";

    protected override Type Entity => typeof(PushSubscriber);

    protected override string MapCall => "AddRaskWebPush";
}
