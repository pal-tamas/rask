// THE ONE FETCH every native binary Rask's build downloads goes through — Tailwind, tsgo and esbuild (their
// compiled tasks link this file) and Litestream and Chrome for Testing (whose packages ship no task assembly,
// so Rask.FetchVerifiedFile.targets compiles this file as an inline task in the app's own build).
//
// Several types in one file, against the one-type-per-file rule, because RoslynCodeTaskFactory compiles exactly
// ONE source file: split, the inline half could not see the retry the compiled half uses, and the two copies
// would drift. That is also why it uses nothing newer than the inline compiler can be relied on to accept.
#pragma warning disable MA0048 // File name must match type name — one file is all an inline task can compile (above).
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace Rask.Build;

/// <summary>
///     Downloads one file, checks its SHA-256, and moves it into place — asking again, with growing pauses, when
///     the network fails for a reason that passes.
/// </summary>
/// <remarks>
///     <para>
///         The task is only ever asked for a file that is not cached yet: the caller looks first, so a cache hit
///         never touches the network. What reaches here is the first fetch on a machine (or a CI runner whose
///         cache was not restored), which is exactly when a release host answering 504 used to fail the build.
///     </para>
///     <para>
///         <see cref="Required" /> decides what an unreachable host means. A file this build cannot do without is
///         an error that names the cache path to seed; a file only a later step needs (Litestream, which runs beside
///         the published app) is a warning, and the build goes on. Bytes that arrive and are not the pinned ones
///         are an error either way: that is a changed or tampered release, never a passing outage.
///     </para>
/// </remarks>
public sealed class RaskFetchVerifiedFile : Task
{
    /// <summary>Where the file is downloaded from.</summary>
    [Required]
    public string Url { get; set; } = string.Empty;

    /// <summary>Where the verified file lands. Its directory is created.</summary>
    [Required]
    public string DestinationFile { get; set; } = string.Empty;

    /// <summary>The SHA-256 the file must have, as hex. Empty only when nobody pinned one, which is warned about.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The package the messages are about, e.g. <c>Rask.SQLite.Litestream</c>.</summary>
    public string Product { get; set; } = "Rask";

    /// <summary>The cache directory to name in a failure — the one to seed by hand or keep between CI runs.</summary>
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>What else the user can do when the host stays unreachable (the package's opt-out, say).</summary>
    public string Remedy { get; set; } = string.Empty;

    /// <summary>False when this build can go on without the file: an unreachable host is then a warning.</summary>
    public bool Required { get; set; } = true;

    /// <summary>The pauses between attempts, in seconds, <c>;</c>-separated. Empty for <see cref="TransientRetry.Waits" />.</summary>
    public string RetryWaits { get; set; } = string.Empty;

    /// <summary>Whether the verified file is now at <see cref="DestinationFile" />.</summary>
    [Output]
    public bool Fetched { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        if (File.Exists(DestinationFile))
        {
            Fetched = true;
            return true;
        }

        var waits = TransientRetry.Parse(RetryWaits);
        try
        {
            Log.LogMessage(MessageImportance.High, $"{Product}: fetching {Url} (once; cached in {Where()})…");
            Download(waits);
            Fetched = true;
            return true;
        }
        catch (InvalidDataException ex)
        {
            Log.LogError($"{Product}: {Url} {ex.Message}. Nothing was installed. A proxy that rewrites downloads, or a "
                         + "republished release: check it before pinning a new digest.");
            return false;
        }
        catch (Exception ex) when (TransientRetry.IsTransient(ex) || ex is HttpRequestException or IOException
                                                                         or UnauthorizedAccessException)
        {
            var tried = TransientRetry.IsTransient(ex) ? $" after {waits.Count + 1} attempts" : string.Empty;
            var message =
                $"{Product}: could not download {Url}{tried} ({ex.Message}). Put the file at '{DestinationFile}' "
                + $"yourself (SHA-256 {(Sha256.Length == 0 ? "unpinned" : Sha256.ToLowerInvariant())}), or keep '{Where()}' "
                + "between CI runs so a release host's outage never reaches the build (actions/cache: see "
                + "https://rask.sh/docs/guides/deployment#caching-the-builds-tools-in-ci)."
                + (Remedy.Length == 0 ? string.Empty : " " + Remedy);
            if (Required)
            {
                Log.LogError(message);
                return false;
            }

            Log.LogWarning(message);
            return true;
        }
    }

    private string Where() => CacheDirectory.Length == 0 ? Path.GetDirectoryName(DestinationFile) ?? DestinationFile : CacheDirectory;

    private void Download(IReadOnlyList<TimeSpan> waits)
    {
        if (Sha256.Length == 0)
        {
            Log.LogWarning($"{Product}: no SHA-256 is pinned for {Url}, so the download is not verified.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(DestinationFile))!;
        Directory.CreateDirectory(directory);

        // Downloaded beside the destination under a name of its own and moved into place once verified: a
        // cancelled build leaves no truncated file that a later build mistakes for a cache hit, and two builds
        // fetching at once (a solution, a test run) never write into one file.
        var partial = DestinationFile + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                TransientRetry.Run(
                    () => HttpFetch.ToFile(http, Url, partial),
                    waits,
                    (ex, next) => Log.LogMessage(
                        MessageImportance.High,
                        $"{Product}: fetching {Url} failed ({ex.Message}); asking again, attempt {next} of {waits.Count + 1}…"));
            }

            Verify(partial);
            MoveIntoPlace(partial, DestinationFile);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private void Verify(string path)
    {
        if (Sha256.Length == 0)
        {
            return;
        }

        var actual = HttpFetch.Sha256Hex(path);
        if (!string.Equals(actual, Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"has SHA-256 {actual}, not the pinned {Sha256.Trim().ToLowerInvariant()}");
        }
    }

    // Whoever gets there first wins and the other keeps that copy: both verified the same digest, and the winner's
    // file may already be in use by the other build.
    private static void MoveIntoPlace(string partial, string destination)
    {
        if (File.Exists(destination))
        {
            return;
        }

        try
        {
            File.Move(partial, destination);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Another build moved its own verified copy in between the check and the move.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A stray partial is litter in a cache, never mistaken for the file.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}

/// <summary>An HTTP answer that was not success, carrying its status as a number for deciding whether to ask again.</summary>
internal sealed class HttpAnswerException : HttpRequestException
{
    public HttpAnswerException(int status, string url)
        : base(string.Format(CultureInfo.InvariantCulture, "{0} answered {1}", url, status)) => Status = status;

    public HttpAnswerException()
    {
    }

    public HttpAnswerException(string message)
        : base(message)
    {
    }

    public HttpAnswerException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>The HTTP status; 0 when none was given.</summary>
    public int Status { get; }
}

/// <summary>The few HTTP calls a fetch makes, each failing with an <see cref="HttpAnswerException" /> on a non-success answer.</summary>
internal static class HttpFetch
{
    /// <summary>The body of a successful answer.</summary>
    public static HttpContent Get(HttpClient http, string url, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var response = http.GetAsync(url, completion).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpAnswerException(status, url);
        }

        return response.Content;
    }

    /// <summary>The body of a successful answer, as bytes.</summary>
    public static byte[] Bytes(HttpClient http, string url)
    {
        using var content = Get(http, url);
        return content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    /// <summary>The body of a successful answer, as text.</summary>
    public static string Text(HttpClient http, string url)
    {
        using var content = Get(http, url);
        return content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <summary>Streams a successful answer into <paramref name="path" />, replacing what an earlier attempt left.</summary>
    public static bool ToFile(HttpClient http, string url, string path)
    {
        using var content = Get(http, url, HttpCompletionOption.ResponseHeadersRead);
        using var body = content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        body.CopyTo(file);
        return true;
    }

    /// <summary>The lower-case hex SHA-256 of a file.</summary>
    public static string Sha256Hex(string path)
    {
        using var sha = SHA256.Create();
        using var file = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty).ToLowerInvariant();
    }
}

/// <summary>
///     Asks the network again when it failed for a reason that passes — a dropped connection, a timeout, a host
///     answering 5xx, 408 or 429 — and gives up at once on an answer that will not change, such as 404.
/// </summary>
internal static class TransientRetry
{
    /// <summary>
    ///     The pauses between attempts: seven attempts in all, 90 seconds of waiting. Long enough to ride out a
    ///     release host serving random 502/504s, short enough that a real outage fails the build in under two minutes.
    /// </summary>
    internal static readonly IReadOnlyList<TimeSpan> Waits =
        new[]
        {
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
        };

    private static readonly char[] Separators = { ';', ',' };

    /// <summary><paramref name="seconds" /> as pauses (<c>2;5;15</c>), or <see cref="Waits" /> when empty.</summary>
    internal static IReadOnlyList<TimeSpan> Parse(string seconds)
    {
        if (string.IsNullOrWhiteSpace(seconds))
        {
            return Waits;
        }

        var waits = new List<TimeSpan>();
        foreach (var part in seconds.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            waits.Add(TimeSpan.FromSeconds(double.Parse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)));
        }

        return waits;
    }

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
        pause ??= wait => System.Threading.Tasks.Task.Delay(wait).GetAwaiter().GetResult(); // an MSBuild task runs synchronously
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
        HttpAnswerException { Status: var status } => status >= 500 || status is 408 or 429,
        HttpRequestException => true, // no answer at all: DNS, a refused or reset connection
        TaskCanceledException => true, // HttpClient's timeout
        IOException { InnerException: not null } => true, // the connection broke mid-download
        _ => false,
    };
}
