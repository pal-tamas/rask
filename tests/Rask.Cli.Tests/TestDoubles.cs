using System.Collections.Concurrent;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// An <see cref="IConsole"/> that captures everything written, for assertions. Both output streams
/// report as redirected so styling stays off and captured text is plain — substring assertions hold
/// regardless of the styling layer. Seed <see cref="InputLines"/> to script interactive prompts.
/// </summary>
internal sealed class StringConsole : IConsole
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private TextReader _in = new StringReader(string.Empty);

    public TextWriter Out => _out;

    public TextWriter Error => _error;

    public TextReader In => _in;

    /// <summary>When false, the console behaves like a terminal (styling on, prompts read input).</summary>
    public bool IsOutputRedirected { get; set; } = true;

    public bool IsErrorRedirected { get; set; } = true;

    public bool IsInputRedirected { get; set; } = true;

    public string OutText => _out.ToString();

    public string ErrorText => _error.ToString();

    /// <summary>Script the answers an interactive prompt will read, one per line, and treat stdin as a terminal.</summary>
    public IReadOnlyList<string> InputLines
    {
        set
        {
            _in = new StringReader(string.Join(Environment.NewLine, value) + Environment.NewLine);
            IsInputRedirected = false;
        }
    }
}

/// <summary>A recorded invocation of the process runner.</summary>
internal sealed record ProcessInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool Captured,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? WorkingDirectory = null);

/// <summary>
/// A fake <see cref="IProcessRunner"/>: records every invocation and returns scripted results, so command
/// tests assert on the exact <c>dotnet</c> command line without spawning a process.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly ConcurrentQueue<ProcessInvocation> _invocations = new();

    /// <summary>Exit code returned by <see cref="RunAsync"/> (fallback when <see cref="RunHandler"/> is unset).</summary>
    public int RunExitCode { get; set; }

    /// <summary>Result returned by <see cref="CaptureAsync"/> (fallback when <see cref="CaptureHandler"/> is unset).</summary>
    public ProcessResult CaptureResult { get; set; } = new(0, string.Empty, string.Empty);

    /// <summary>Per-invocation run exit code by argument list — for multi-step flows where one step fails.</summary>
    public Func<IReadOnlyList<string>, int>? RunHandler { get; set; }

    /// <summary>Per-invocation capture result by argument list — for flows that read varied command output.</summary>
    public Func<IReadOnlyList<string>, ProcessResult>? CaptureHandler { get; set; }

    public IReadOnlyList<ProcessInvocation> Invocations => _invocations.ToArray();

    public ProcessInvocation? LastRun => Invocations.LastOrDefault(i => !i.Captured);

    public Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var args = arguments.ToArray();
        _invocations.Enqueue(new ProcessInvocation(fileName, args, Captured: false, environment, workingDirectory));
        return Task.FromResult(RunHandler?.Invoke(args) ?? RunExitCode);
    }

    /// <summary>Lines the fake feeds to the tee's observer, so a test can drive the build watcher.</summary>
    public IReadOnlyList<string> TeeLines { get; set; } = [];

    /// <summary>
    /// Runs while the command still believes the child process is alive, after <see cref="TeeLines"/> have
    /// been fed. The only way to observe anything a command owns for the lifetime of the run — `rask dev`
    /// disposes its status server the moment the run returns.
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>?, Task>? DuringRunAsync { get; set; }

    public async Task<int> RunTeeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var args = arguments.ToArray();
        _invocations.Enqueue(new ProcessInvocation(fileName, args, Captured: false, environment, workingDirectory));
        foreach (var line in TeeLines)
        {
            onLine(line);
        }

        if (DuringRunAsync is not null)
        {
            await DuringRunAsync(environment).ConfigureAwait(false);
        }

        return RunHandler?.Invoke(args) ?? RunExitCode;
    }

    public Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var args = arguments.ToArray();
        _invocations.Enqueue(new ProcessInvocation(fileName, args, Captured: true));
        return Task.FromResult(CaptureHandler?.Invoke(args) ?? CaptureResult);
    }
}

/// <summary>An in-memory <see cref="IFileSystem"/> for scaffolding tests — no disk access.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _written = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    /// <summary>Files that exist now, keyed by normalized absolute path.</summary>
    public IReadOnlyDictionary<string, string> Files => _files;

    /// <summary>
    /// Everything ever written, including files since deleted. Lets a test assert on the content of a
    /// deliberately short-lived file — the generated Caddyfile is copied to the host and then removed.
    /// </summary>
    public IReadOnlyDictionary<string, string> Written => _written;

    public void Seed(string path, string content = "")
    {
        var full = Normalize(path);
        _files[full] = content;
        _written[full] = content;
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

    public IReadOnlyList<string> ListFilesRecursive(string directory, string searchPattern)
    {
        var dir = Normalize(directory);
        var suffix = searchPattern.StartsWith('*') ? searchPattern[1..] : searchPattern;
        return _files.Keys
            .Where(f => (f.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Normalize(Path.GetDirectoryName(f)!) == dir)
                && f.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
    }

    public string ReadAllText(string path) => _files[Normalize(path)];

    public void CreateDirectory(string path) => _directories.Add(Normalize(path));

    public void WriteAllText(string path, string content)
    {
        _files[Normalize(path)] = content;
        _written[Normalize(path)] = content;
        _directories.Add(Normalize(Path.GetDirectoryName(path)!));
    }

    public void TryDelete(string path) => _files.Remove(Normalize(path));

    private static string Normalize(string path) => Path.GetFullPath(path);
}


/// <summary>
/// A fake <see cref="IBrowserLauncher"/>: records the URLs it was asked to open, so `--open` is asserted
/// without CI ever spawning a browser.
/// </summary>
internal sealed class FakeBrowserLauncher : IBrowserLauncher
{
    public List<string> Opened { get; } = [];

    /// <summary>Set false to simulate a platform where the open command could not be started.</summary>
    public bool Succeeds { get; set; } = true;

    public Task<bool> TryOpenAsync(string url, CancellationToken cancellationToken)
    {
        Opened.Add(url);
        return Task.FromResult(Succeeds);
    }
}
