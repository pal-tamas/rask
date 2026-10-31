using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Xunit.Sdk;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     The steps a test has taken, each stamped with when it began, so a failure names the step that failed — or
///     hung — and carries what the host logged on the way there, on one timeline.
/// </summary>
/// <remarks>
///     For a test that fails once in hundreds of runs: there is no second look at that run, so the failure message
///     itself has to say where it stood.
/// </remarks>
internal sealed class StepTrail(CapturedLog log)
{
    private readonly ConcurrentQueue<(DateTimeOffset At, string Step)> _steps = new();
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

    /// <summary>Notes a step that cannot fail by itself, so a later failure shows it was taken.</summary>
    public void Mark(string step) => _steps.Enqueue((DateTimeOffset.UtcNow, step));

    /// <summary>Takes <paramref name="step" />; a failure in it is rethrown naming it, the trail and the log.</summary>
    public async Task Run(string step, Func<Task> action) =>
        await Run(step, async () =>
        {
            await action();
            return true;
        });

    /// <inheritdoc cref="Run(string, Func{Task})" />
    public async Task<T> Run<T>(string step, Func<Task<T>> action)
    {
        Mark(step);
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            throw new XunitException(Describe(step, ex), ex);
        }
    }

    private string Describe(string step, Exception ex)
    {
        var lines = _steps.Select(s => (s.At, Line: "STEP " + s.Step))
            .Concat(log.Entries.Select(e => (e.At, Line: $"  log [{e.Level}] {e.Category}: {e.Message}"
                                                         + (e.Exception is null ? "" : Environment.NewLine + e.Exception))))
            .OrderBy(l => l.At);

        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"The step '{step}' failed after {Since(DateTimeOffset.UtcNow)}: {ex.Message}")
            .AppendLine()
            .AppendLine("Timeline (ms since the test began):");
        foreach (var (at, line) in lines)
        {
            text.Append(CultureInfo.InvariantCulture, $"{Since(at),9}  ").AppendLine(line);
        }

        return text.ToString();
    }

    private string Since(DateTimeOffset at) =>
        ((at - _start).TotalMilliseconds).ToString("0", CultureInfo.InvariantCulture) + " ms";
}
