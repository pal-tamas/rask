using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Cli.Commands;

/// <summary>
/// What a stopping app is still waiting for, read from its log. <c>Rask.Jobs</c> logs
/// <c>Stopping: no new job is claimed; waiting for 1 running job(s) to finish: AtPump (3m12s)</c> every few
/// seconds while it drains; the log is the one signal every image has, whatever its base or its tools.
/// </summary>
internal static partial class StopProgress
{
    /// <summary>The last report in <paramref name="logs"/> as <c>1 job: AtPump (3m12s)</c>, or <c>null</c> when there is none.</summary>
    internal static string? RunningJobs(string logs)
    {
        var reports = Report().Matches(logs);
        if (reports.Count == 0)
        {
            return null;
        }

        var last = reports[^1];
        var count = int.Parse(last.Groups["count"].Value, CultureInfo.InvariantCulture);
        return $"{count} job{(count == 1 ? string.Empty : "s")}: {last.Groups["jobs"].Value.Trim()}";
    }

    // Plain text in the simple console format, a JSON string in the json one: the jobs end at a quote or the line.
    [GeneratedRegex(@"waiting for (?<count>\d+) running job\(s\) to finish: (?<jobs>[^""\r\n]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Report();
}
