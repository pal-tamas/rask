using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Names the <see cref="DbContext" /> the model surface opens, registered by
///     <c>AddRaskData&lt;TContext&gt;()</c> and read by <see cref="Db.Configure(IServiceProvider)" />.
/// </summary>
/// <remarks>
///     A registration rather than a scan: an app with two contexts has to be told which one the bare
///     <c>Product.Where(…)</c> means, and a convention that picked the first would pick differently as
///     the app grew.
/// </remarks>
public sealed class AmbientContextBinding
{
    private readonly Func<DbContext> _createContext;

    internal AmbientContextBinding(Type contextType, Func<DbContext> createContext)
    {
        ContextType = contextType;
        _createContext = createContext;
    }

    /// <summary>The bound context type.</summary>
    public Type ContextType { get; }

    /// <summary>Opens a fresh, unshared context. The caller — one read or one write — owns and disposes it.</summary>
    public DbContext CreateContext() => _createContext();
}
