using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data.Tests;

/// <summary>
///     This suite builds every context shape the conventions cover — plain and Rask contexts, the read side, the container's
///     interceptors, a test database's — so EF Core's count of the internal service providers one process builds passes
///     its twenty here by design, not by a leak. A class whose context is built late in the run says so.
/// </summary>
internal static class ManyContextShapes
{
    public static DbContextOptionsBuilder OneOfManyShapes(this DbContextOptionsBuilder builder) =>
        builder.ConfigureWarnings(static w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

    public static DbContextOptionsBuilder<TContext> OneOfManyShapes<TContext>(this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.ConfigureWarnings(static w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return builder;
    }
}
