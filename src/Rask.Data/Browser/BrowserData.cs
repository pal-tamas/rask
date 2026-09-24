using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Live;

namespace Rask.Data;

/// <summary>
///     What a Rask server host wires for Rask.Data, done by <c>AddRaskData</c> itself in a WebAssembly app: a save
///     refreshes the page's queries about what it wrote, with no app code.
/// </summary>
/// <remarks>
///     Here rather than in the <c>Rask</c> package's browser half, which the trimmer keeps whole: a reference to
///     Rask.Data from there would put EF Core in every WASM bundle, including apps with no database.
/// </remarks>
internal static class BrowserData
{
    internal static void Add(IServiceCollection services)
    {
        services.TryAddSingleton<ISessionWorkScope, BrowserDataScope>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDataChanges, QueryDataChanges>());
    }
}

/// <summary>
///     The page's services made ambient around its work — where a save finds <see cref="QueryDataChanges" /> —
///     and, the first time, the model surface pointed at the app's context.
/// </summary>
/// <remarks>
///     The first entry is the first render, so <c>Db.Configure</c> has run before any page reads and before the
///     hosted services start, which the browser host does after that render. A server host calls it after
///     building its container instead.
/// </remarks>
internal sealed class BrowserDataScope : ISessionWorkScope
{
    private int _configured;

    public IDisposable? Enter(IServiceProvider sessionServices)
    {
        if (Interlocked.Exchange(ref _configured, 1) == 0 &&
            sessionServices.GetService<AmbientContextBinding>() is not null)
        {
            Db.Configure(sessionServices);
        }

        return Db.UseScope(sessionServices);
    }
}
