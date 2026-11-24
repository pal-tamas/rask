using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>Options laid out the way <c>AddDbContext</c> and <c>AddDbContextFactory</c> lay them out.</summary>
/// <remarks>
///     EF Core keys a context's internal services on its options' extensions in the order they were added, and refuses
///     a process that builds more than twenty sets. <c>AddDbContext</c> adds its own (Core) before the app's lambda
///     runs; options built the same way share the set an app's registered factory built.
/// </remarks>
internal static class OptionsLayout
{
    /// <summary>A builder whose first extension is Core, as a registered factory's is.</summary>
    /// <typeparam name="TContext">The context the options are for.</typeparam>
    /// <returns>The builder, with nothing configured but where its Core extension sits.</returns>
    public static DbContextOptionsBuilder<TContext> LikeAddDbContext<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().EnableServiceProviderCaching();
}
