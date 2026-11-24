using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Ends the loading <see cref="WritableChildrenInterceptor" /> began for an entity whose materialization threw, so
///     a query that failed on a stored row never leaves the next <c>Create</c> on its thread unchecked.
/// </summary>
/// <remarks>
///     EF Core reports a failed query iteration (<see cref="CoreEventId.QueryIterationFailed" />) to its diagnostic
///     listener synchronously, on the thread that was materializing, before it rethrows — the one place that sees
///     the failure of ANY query, an app's own through an injected context included. Only that event is asked for,
///     so EF Core builds no event data for any other.
/// </remarks>
internal static class FailedMaterializations
{
    private static readonly string Failed = CoreEventId.QueryIterationFailed.Name ?? "Microsoft.EntityFrameworkCore.Query.QueryIterationFailed";

    private static int _watching;

    /// <summary>Starts watching EF Core's listeners, once per process.</summary>
    public static void Watch()
    {
        if (Interlocked.Exchange(ref _watching, 1) == 0)
        {
            DiagnosticListener.AllListeners.Subscribe(new Listeners());
        }
    }

    private sealed class Listeners : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (string.Equals(value.Name, DbLoggerCategory.Name, StringComparison.Ordinal))
            {
                value.Subscribe(new Failures(), static name => string.Equals(name, Failed, StringComparison.Ordinal));
            }
        }

        public void OnError(Exception error)
        {
            // Nothing to end: a listener that fails is not one EF Core reports a query through.
        }

        public void OnCompleted()
        {
            // Nothing to end: the process is going.
        }
    }

    private sealed class Failures : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (string.Equals(value.Key, Failed, StringComparison.Ordinal))
            {
                RowLoading.Abandon();
            }
        }

        public void OnError(Exception error)
        {
            // Nothing to end: no failure was reported.
        }

        public void OnCompleted()
        {
            // Nothing to end: the listener's context is gone.
        }
    }
}
