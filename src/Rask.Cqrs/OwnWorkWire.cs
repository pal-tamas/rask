using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     How the work its enqueuer watches crosses to a browser (#1348): <see cref="ReadOwnWork" /> asks the server for the
///     rows, and <see cref="WatchOwnWork" /> opens there, so the bell of an app's browser half shows the same work, under the
///     same rules, as a server page's.
/// </summary>
/// <remarks>
///     <para>
///         Installed by the two halves that use it — the server's <c>AddRaskJobs</c>, which answers them, and the browser's
///         batteries, which send them — and by nothing else, so a server without jobs answers both with the 404 of a name it
///         does not know.
///     </para>
///     <para>
///         Neither carries a user. The server reads who asks in the request's own scope — the board's read and
///         <c>OwnWorkPolicy</c>'s stamp — and both refuse a caller nobody signed in: the endpoint first, the policy and the
///         board again behind it.
///     </para>
/// </remarks>
internal static class OwnWorkWire
{
    /// <summary>The rows: a GET the signed-in caller alone may send.</summary>
    internal static readonly RemoteContract Rows = new()
    {
        MessageType = typeof(ReadOwnWork),
        Name = "Rask.Background.ReadOwnWork",
        Kind = RemoteMessageKind.Query,
        ResultType = typeof(OwnWorkRead),
        WriteMessage = static (writer, message, _) => OwnWorkJson.WriteLane(writer, ((ReadOwnWork)message).Lane),
        ReadMessage = static (ref reader, _) => new ReadOwnWork(OwnWorkJson.ReadLane(ref reader)),
        WriteResult = static (writer, result) => OwnWorkJson.WriteRead(writer, (OwnWorkRead)result!),
        ReadResult = static (ref reader) => OwnWorkJson.ReadRead(ref reader),
        LocalInvoker = static async (provider, message, cancellationToken) =>
            await Answer(provider, (ReadOwnWork)message, cancellationToken).ConfigureAwait(false),
        RequiresAuthentication = true,
    };

    /// <summary>The live reports: opened on the server and admitted there by its watch policy.</summary>
    internal static readonly RemoteContract Watch = new()
    {
        MessageType = typeof(WatchOwnWork),
        Name = "Rask.Background.WatchOwnWork",
        Kind = RemoteMessageKind.Subscription,
        ResultType = typeof(WorkReported),
        WriteMessage = static (writer, message, _) => OwnWorkJson.WriteLane(writer, ((WatchOwnWork)message).Lane),
        ReadMessage = static (ref reader, _) => new WatchOwnWork(OwnWorkJson.ReadLane(ref reader)),
        WriteResult = static (writer, result) => OwnWorkJson.WriteWork(writer, (WorkReported)result!),
        ReadResult = static (ref reader) => OwnWorkJson.ReadWork(ref reader),
    };

    private static readonly RemoteContract[] Contracts = [Rows, Watch];

    /// <summary>Makes both crossable from this process. Once is enough; again changes nothing.</summary>
    internal static void Install() => RemoteContractRegistry.Replace(typeof(OwnWorkWire), Contracts);

    // The server's half: the board of the request's own scope, which reads who asks there. With none — no Rask.Jobs —
    // there is no work to show.
    private static async Task<OwnWorkRead> Answer(IServiceProvider provider, ReadOwnWork read, CancellationToken cancellationToken)
    {
        if (provider.GetService<IWorkBoard>() is not { } board)
        {
            return new OwnWorkRead(TimeSpan.Zero, []);
        }

        var work = await board.Mine(read.Lane, cancellationToken).ConfigureAwait(false);
        return new OwnWorkRead(board.Every, work);
    }
}
