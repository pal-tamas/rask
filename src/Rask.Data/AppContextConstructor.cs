using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Builds an app's context the way the app does: through its public constructor taking its options, over the model
///     the app's assembly registers.
/// </summary>
internal static class AppContextConstructor
{
    /// <summary>The constructor taking <c>DbContextOptions&lt;TContext&gt;</c>, or else one taking <c>DbContextOptions</c>.</summary>
    /// <remarks>
    ///     Runs the context's assembly's module initializer first: it is what registers the app's aggregates, and the
    ///     runtime runs it only once something of that assembly is touched. A test whose first line starts its database
    ///     has touched nothing of it, and was handed a model of none — a database without a table.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext" /> has neither.</exception>
    public static Func<DbContextOptions<TContext>, TContext> For<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>()
        where TContext : DbContext
    {
        var type = typeof(TContext);
        RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
        var constructor = type.GetConstructor([typeof(DbContextOptions<TContext>)])
            ?? type.GetConstructor([typeof(DbContextOptions)])
            ?? throw new InvalidOperationException(
                $"TestDatabase<{type.Name}> builds {type.Name} the way the app does, through a public constructor "
                + $"taking its options, and {type.Name} has none. Add one: "
                + $"public {type.Name}(DbContextOptions<{type.Name}> options) : base(options) {{ }}");

        return options => (TContext)constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, [options], culture: null);
    }
}
