using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>Builds an app's context the way the app does: through its public constructor taking its options.</summary>
internal static class AppContextConstructor
{
    /// <summary>The constructor taking <c>DbContextOptions&lt;TContext&gt;</c>, or else one taking <c>DbContextOptions</c>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext" /> has neither.</exception>
    public static Func<DbContextOptions<TContext>, TContext> For<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>()
        where TContext : DbContext
    {
        var type = typeof(TContext);
        var constructor = type.GetConstructor([typeof(DbContextOptions<TContext>)])
            ?? type.GetConstructor([typeof(DbContextOptions)])
            ?? throw new InvalidOperationException(
                $"TestDatabase.Start<{type.Name}> builds {type.Name} the way the app does, through a public constructor "
                + $"taking its options, and {type.Name} has none. Add one: "
                + $"public {type.Name}(DbContextOptions<{type.Name}> options) : base(options) {{ }}");

        return options => (TContext)constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, [options], culture: null);
    }
}
