using System.Diagnostics;
using System.IO.Compression;

namespace Rask.Cli;

/// <summary>The real process runner, backed by <see cref="Process"/>.</summary>
/// <remarks>
///     The two writers exist for <see cref="RunTeeAsync"/>, the one path that redirects the child's
///     streams and has to hand them back. They default to the real console; a test passes its own so it
///     can assert that a teed line still reaches the developer's terminal, without redirecting the
///     process-global <see cref="Console"/> out from under every test running beside it.
/// </remarks>
internal sealed class ProcessRunner(TextWriter? output = null, TextWriter? error = null) : IProcessRunner
{
    public async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: false, environment);
        await WaitOrKillAsync(process, cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task WaitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Waiting stopped; the child did not. Disposing a Process does not end it either, so without
            // this a cancelled run leaks the whole tree — a bundler still holding its port, a watcher still
            // rebuilding, a `docker -H ssh://` past its deadline still holding its ssh session open.
            TryKillTree(process);
            throw;
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and the kill. Which is the outcome we wanted.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not ours to kill, or already reaped. Nothing better to do, and throwing here would replace
            // the caller's cancellation with a less useful failure.
        }
        catch (NotSupportedException)
        {
            // A process this platform will not enumerate a tree for.
        }
    }

    public async Task<ProcessResult> CaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(fileName, arguments, workingDirectory, redirect: true, environment);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitOrKillAsync(process, cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public async Task<int> RunTeeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(onLine);

        using var process = Start(fileName, arguments, workingDirectory, redirect: true, environment);

        var stdout = PumpAsync(process.StandardOutput, output ?? Console.Out, onLine, cancellationToken);
        var stderr = PumpAsync(process.StandardError, error ?? Console.Error, onLine, cancellationToken);

        await WaitOrKillAsync(process, cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return process.ExitCode;
    }

    public async Task<int> PipeAsync(
        string fileName,
        IReadOnlyList<string> source,
        IReadOnlyList<string> sink,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        using var reader = Start(fileName, source, workingDirectory, redirect: false, redirectOutput: true);
        using var writer = StartSink(reader, fileName, sink, workingDirectory);
        try
        {
            await CompressAsync(reader.StandardOutput.BaseStream, writer.StandardInput.BaseStream, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The sink stopped reading: it failed, and says why on its own standard error. The source has nobody to
            // write to any more, so it goes too; the exit codes below say which side it was.
            TryKillTree(reader);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(reader);
            TryKillTree(writer);
            throw;
        }

        await WaitOrKillAsync(reader, cancellationToken).ConfigureAwait(false);
        await WaitOrKillAsync(writer, cancellationToken).ConfigureAwait(false);
        return reader.ExitCode != 0 ? reader.ExitCode : writer.ExitCode;
    }

    // A sink that cannot start leaves the source writing into a pipe nobody reads: it is ended before the failure goes up.
    private static Process StartSink(Process source, string fileName, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        try
        {
            return Start(fileName, arguments, workingDirectory, redirect: false, redirectInput: true);
        }
        catch
        {
            TryKillTree(source);
            throw;
        }
    }

    // Closing the compressor closes the sink's standard input, which is how it learns the stream has ended.
    private static async Task CompressAsync(Stream from, Stream to, CancellationToken cancellationToken)
    {
        var gzip = new GZipStream(to, CompressionLevel.Fastest);
        await using (gzip.ConfigureAwait(false))
        {
            await from.CopyToAsync(gzip, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PumpAsync(
        StreamReader source,
        TextWriter sink,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        while (await source.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            // Straight through first: the developer's view of watch must not lag behind our bookkeeping,
            // and an observer that throws must not swallow the line it was given.
            await sink.WriteLineAsync(line).ConfigureAwait(false);

            try
            {
                onLine(line);
            }
            catch
            {
                // The observer is a convenience over somebody else's output. It does not get to kill the
                // pump, which would silently truncate the console the developer is reading.
            }
        }
    }

    private static Process Start(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool redirect,
        IReadOnlyDictionary<string, string>? environment = null,
        bool redirectOutput = false,
        bool redirectInput = false)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirect || redirectOutput,
            RedirectStandardError = redirect,
            RedirectStandardInput = redirectInput,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        // ArgumentList quotes each argument for the platform — never hand-concatenate a command line.
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Overlay, not replacement: the child still inherits PATH, HOME and the rest.
        // UseShellExecute is already false, which is what makes ProcessStartInfo.Environment honoured.
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = info };
        process.Start();
        return process;
    }
}
