using System.Collections.Concurrent;

namespace Rask.Cli.Tests;

/// <summary>An <see cref="IConsole"/> that captures everything written, for assertions.</summary>
internal sealed class StringConsole : IConsole
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public TextWriter Out => _out;

    public TextWriter Error => _error;

    public string OutText => _out.ToString();

    public string ErrorText => _error.ToString();
}

/// <summary>A recorded invocation of the process runner.</summary>
internal sealed record ProcessInvocation(string FileName, IReadOnlyList<string> Arguments, bool Captured);

/// <summary>
/// A fake <see cref="IProcessRunner"/>: records every invocation and returns scripted results, so command
/// tests assert on the exact <c>dotnet</c> command line without spawning a process.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly ConcurrentQueue<ProcessInvocation> _invocations = new();

    /// <summary>Exit code returned by <see cref="RunAsync"/>.</summary>
    public int RunExitCode { get; set; }

    /// <summary>Result returned by <see cref="CaptureAsync"/>.</summary>
    public ProcessResult CaptureResult { get; set; } = new(0, string.Empty, string.Empty);

    public IReadOnlyList<ProcessInvocation> Invocations => _invocations.ToArray();

    public ProcessInvocation? LastRun => Invocations.LastOrDefault(i => !i.Captured);

    public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        _invocations.Enqueue(new ProcessInvocation(fileName, arguments.ToArray(), Captured: false));
        return Task.FromResult(RunExitCode);
    }

    public Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        _invocations.Enqueue(new ProcessInvocation(fileName, arguments.ToArray(), Captured: true));
        return Task.FromResult(CaptureResult);
    }
}
