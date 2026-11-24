using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data;

/// <summary>
///     The interceptor of a kind a context was built with, for a write no save surrounds (<c>UpdateWhere</c>,
///     <c>DeleteWhere</c>) to tell it what a save would have: what was written, and to whom.
/// </summary>
internal static class BulkInterceptors
{
    /// <summary>The <typeparamref name="TInterceptor" /> <paramref name="context" /> runs, or <see langword="null" /> when it runs none.</summary>
    internal static TInterceptor? Of<TInterceptor>(DbContext context)
        where TInterceptor : class =>
        context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .Interceptors?
            .OfType<TInterceptor>()
            .FirstOrDefault();
}
