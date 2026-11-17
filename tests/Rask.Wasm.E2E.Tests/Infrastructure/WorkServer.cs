using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Cqrs;
using Rask.Cqrs.Server;
using Rask.Data;

namespace Rask.Wasm.E2E.Tests.Infrastructure;

/// <summary>
///     The server half of a browser app's bell (#1348), as a Rask server with jobs is: the CQRS endpoints, the board the
///     app reads its user's rows from and the watch policy <c>AddRaskJobs</c> registers — with rows a test sets instead of a
///     jobs table, read again only every ten minutes, so whatever moves on the page between reads came over the watch.
/// </summary>
internal sealed class WorkServer
{
    private volatile IReadOnlyList<WorkReported> _rows = [];

    /// <summary>How often the app reads the rows again: rarely enough that a moving bar is the live watch's doing.</summary>
    public static TimeSpan Every => TimeSpan.FromMinutes(10);

    /// <summary>The rows the board answers with, each shown only to its <see cref="WorkReported.Owner" />.</summary>
    public void Rows(params WorkReported[] rows) => _rows = rows;

    /// <summary>Publishes <paramref name="work" /> as a job's report is: to every open watch, each admitted by its policy.</summary>
    public static Task Publish(SignInHost host, WorkReported work) =>
        host.Services.GetRequiredService<IDispatcher>().Publish(work, CancellationToken.None);

    internal void AddTo(IServiceCollection services)
    {
        OwnWorkWire.Install();
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddRaskCqrsServer();
        services.AddScoped<IPrincipalSource, RequestPrincipal>();
        services.AddScoped<IWorkBoard>(sp => new Board(this, sp.GetRequiredService<IPrincipalSource>()));
        services.AddScoped<IWatchPolicy<WatchOwnWork>>(static scope => new OwnWorkPolicy(scope));
    }

    internal static void MapOn(WebApplication app) => app.MapRaskCqrs();

    // Who the request carries, as Rask.Server's principal source hands it to the data layer.
    private sealed class RequestPrincipal(IHttpContextAccessor http) : IPrincipalSource
    {
        public ClaimsPrincipal? Current => http.HttpContext?.User;
    }

    // The rows of whoever asks, as the jobs board reads them: by the signed-in user of the request's own scope.
    private sealed class Board(WorkServer server, IPrincipalSource principal) : IWorkBoard
    {
        public TimeSpan Every => WorkServer.Every;

        public Task<IReadOnlyList<WorkReported>> Mine(string? lane, CancellationToken cancellationToken)
        {
            var who = Guid.TryParse(principal.Current?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
            IReadOnlyList<WorkReported> mine = who is null ? [] : [.. server._rows.Where(w => w.Owner == who)];
            return Task.FromResult(mine);
        }
    }
}
