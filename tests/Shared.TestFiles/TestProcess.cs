using System.Diagnostics;

namespace Rask.TestFiles;

/// <summary>What a process run by <see cref="TestProcess" /> left behind.</summary>
internal sealed record TestProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Both streams, stdout first — what a failure message should show.</summary>
    public string Output => StandardOutput + StandardError;
}

/// <summary>
///     Runs a process to completion for a test: both streams read concurrently, and a timeout that kills the tree.
/// </summary>
/// <remarks>
///     Linked into every <c>*.Tests</c> project. The copies it replaced read stdout to the end and THEN stderr, so a child
///     that filled its stderr pipe first blocked on the write while the test blocked on the read — a hang, not a failure
///     — and none of them had a timeout to turn that hang into a red.
/// </remarks>
internal static class TestProcess
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Runs <paramref name="fileName" /> with each argument passed as-is (no shell splitting).</summary>
    public static Task<TestProcessResult> Run(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var start = Start(fileName, workingDirectory, environment);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return Run(start, timeout ?? DefaultTimeout, cancellationToken);
    }

    /// <summary>Runs <paramref name="fileName" /> with one pre-joined argument string.</summary>
    public static Task<TestProcessResult> Run(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var start = Start(fileName, workingDirectory, environment);
        start.Arguments = arguments;
        return Run(start, timeout ?? DefaultTimeout, cancellationToken);
    }

    private static ProcessStartInfo Start(
        string fileName, string? workingDirectory, IReadOnlyDictionary<string, string?>? environment)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
        {
            start.Environment[key] = value;
        }

        return start;
    }

    private static async Task<TestProcessResult> Run(ProcessStartInfo start, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException($"'{start.FileName}' did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"'{start.FileName} {start.Arguments}{string.Join(' ', start.ArgumentList)}' did not exit within {timeout}.");
        }

        return new TestProcessResult(process.ExitCode, await stdout, await stderr);
    }
}
