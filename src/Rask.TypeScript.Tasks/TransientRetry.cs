using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Rask.TypeScript.Tasks;

/// <summary>
///     Asks the network again when it failed for a reason that passes — a dropped connection, a timeout, a
///     registry answering 5xx, 408 or 429 — and gives up at once on an answer that will not change, such as 404.
/// </summary>
internal static class TransientRetry
{
    /// <summary>The waits between attempts: three attempts in all, about seven seconds at worst.</summary>
    internal static readonly IReadOnlyList<TimeSpan> Waits = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    /// <summary>Runs <paramref name="attempt" />, again after each wait while it fails transiently.</summary>
    /// <param name="attempt">The network call.</param>
    /// <param name="waits">The pause before each further attempt; its length is the number of retries.</param>
    /// <param name="retrying">Told the failure and the number of the attempt that comes next, before the pause.</param>
    /// <param name="pause">How a pause is spent; a test passes one that does not wait.</param>
    internal static T Run<T>(
        Func<T> attempt,
        IReadOnlyList<TimeSpan> waits,
        Action<Exception, int> retrying,
        Action<TimeSpan>? pause = null)
    {
        pause ??= wait => Task.Delay(wait).GetAwaiter().GetResult(); // an MSBuild task runs synchronously
        var retries = 0;
        while (true)
        {
            try
            {
                return attempt();
            }
            catch (Exception ex) when (retries < waits.Count && IsTransient(ex))
            {
                retrying(ex, retries + 2);
                pause(waits[retries]);
                retries++;
            }
        }
    }

    /// <summary>Whether asking again could get a different answer.</summary>
    internal static bool IsTransient(Exception ex) => ex switch
    {
        RegistryAnswerException { Status: var status } => status >= 500 || status is 408 or 429,
        HttpRequestException => true, // no answer at all: DNS, a refused or reset connection
        TaskCanceledException => true, // HttpClient's timeout
        IOException { InnerException: not null } => true, // the connection broke mid-download
        _ => false,
    };
}
