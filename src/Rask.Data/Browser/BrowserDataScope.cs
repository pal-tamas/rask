using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Live;

namespace Rask.Data;

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
