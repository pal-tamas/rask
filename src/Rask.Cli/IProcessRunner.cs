using System.Diagnostics;

namespace Rask.Cli;

/// <summary>The outcome of a captured child process.</summary>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// The seam through which the tool shells out to the .NET SDK. Every command talks to the outside
/// world only through this interface, so tests drive them with a fake and never spawn a real process.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Run a child process with its standard streams inherited (interactive commands like
    /// <c>rask new</c> / <c>rask dev</c>, whose output the user should see live). Returns the exit code.
    /// </summary>
    Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Run a child process and capture its output (probing commands like <c>rask info</c> that parse
    /// <c>dotnet --version</c> / <c>dotnet new list</c>).
    /// </summary>
    Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);
}

/// <summary>The real process runner, backed by <see cref="Process"/>.</summary>
internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: true);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static Process Start(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, bool redirect)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        // ArgumentList quotes each argument for the platform — never hand-concatenate a command line.
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };
        process.Start();
        return process;
    }
}
