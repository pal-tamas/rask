using System.Collections.Concurrent;
using Rask.Cli.Scaffolding;

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

/// <summary>An in-memory <see cref="IFileSystem"/> for scaffolding tests — no disk access.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    /// <summary>Files written (via WriteAllText), keyed by normalized absolute path.</summary>
    public IReadOnlyDictionary<string, string> Files => _files;

    public void Seed(string path, string content = "")
    {
        var full = Normalize(path);
        _files[full] = content;
        _directories.Add(Normalize(Path.GetDirectoryName(path)!));
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public IReadOnlyList<string> ListFiles(string directory, string searchPattern)
    {
        var dir = Normalize(directory);
        var suffix = searchPattern.StartsWith('*') ? searchPattern[1..] : searchPattern;
        return _files.Keys
            .Where(f => string.Equals(Normalize(Path.GetDirectoryName(f)!), dir, StringComparison.Ordinal)
                && f.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
    }

    public string ReadAllText(string path) => _files[Normalize(path)];

    public void CreateDirectory(string path) => _directories.Add(Normalize(path));

    public void WriteAllText(string path, string content)
    {
        _files[Normalize(path)] = content;
        _directories.Add(Normalize(Path.GetDirectoryName(path)!));
    }

    private static string Normalize(string path) => Path.GetFullPath(path);
}
