using System.Diagnostics;
using System.Text;

namespace Rask.Printing;

/// <summary>
/// One headless Chromium, its DevTools pipe and its throwaway profile. Disposing closes the browser, waits for it
/// and deletes the profile; if Rask's own process dies first, the pipe closes and Chromium exits by itself.
/// </summary>
internal sealed class ChromiumProcess : IAsyncDisposable
{
    // The pipe is fds 3 (commands in) and 4 (replies out). Process cannot hand a child more than stdin, stdout and
    // stderr, so a shell moves the two pipes onto 3 and 4 and then becomes Chromium (exec): the process Rask watches
    // and kills is the browser itself. Chromium's own stdout joins stderr, which is only ever read for the log.
    private const string MovePipesThenExec = "exec \"$0\" \"$@\" 3<&0 4>&1 0</dev/null 1>&2";
    private const int LogLines = 20;
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly DirectoryInfo _profile;
    private readonly Queue<string> _log = new();

    private ChromiumProcess(Process process, DirectoryInfo profile)
    {
        _process = process;
        _profile = profile;
        _process.ErrorDataReceived += (_, line) => Remember(line.Data);
        _process.BeginErrorReadLine();
        Connection = new DevToolsConnection(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, Log);
    }

    public DevToolsConnection Connection { get; }

    /// <summary>Whether this browser can print no more: it exited, crashed, or its pipe broke.</summary>
    public bool IsGone => Connection.IsClosed || _process.HasExited;

    /// <summary>Whether the browser process itself has ended.</summary>
    internal bool HasExited => _process.HasExited;

    /// <summary>The throwaway profile folder this browser keeps its state in.</summary>
    internal string Profile => _profile.FullName;

    /// <summary>Starts <paramref name="binary"/> headless, on a fresh profile that no other process shares.</summary>
    public static ChromiumProcess Start(string binary, bool noSandbox)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Rask.Pdf drives Chromium over its DevTools pipe, which it opens on Linux and macOS. Print from a Linux "
                + "container or a Mac; on Windows, run the app under WSL or in its Linux container.");
        }

        var profile = Directory.CreateTempSubdirectory("rask-pdf-");
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(MovePipesThenExec);
        start.ArgumentList.Add(binary);
        foreach (var flag in Flags(profile.FullName, noSandbox))
        {
            start.ArgumentList.Add(flag);
        }

        try
        {
            return new ChromiumProcess(
                Process.Start(start) ?? throw new InvalidOperationException($"Chromium did not start from '{binary}'."),
                profile);
        }
        catch
        {
            profile.Delete(recursive: true);
            throw;
        }
    }

    /// <summary>Ends the browser now, without asking it — for one that stopped answering.</summary>
    public void Kill()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        using (var closing = new CancellationTokenSource(CloseWait))
        {
            try
            {
                if (!IsGone)
                {
                    await Connection.Send("Browser.close", cancellation: closing.Token).ConfigureAwait(false);
                }

                await _process.WaitForExitAsync(closing.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
            {
                // It closed the pipe before answering, or did not close in time: either way it ends here.
                Kill();
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }

        await Connection.DisposeAsync().ConfigureAwait(false);
        _process.Dispose();
        DeleteProfile();
    }

    private static IEnumerable<string> Flags(string profile, bool noSandbox)
    {
        string[] flags =
        [
            "--headless=new",
            "--remote-debugging-pipe",
            $"--user-data-dir={profile}",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-gpu",
            // A container's /dev/shm is 64 MB; a long document's renderer outgrows it and crashes.
            "--disable-dev-shm-usage",
            "--disable-extensions",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-default-apps",
            "--disable-sync",
            "--mute-audio",
            "--hide-scrollbars",
            // A Mac otherwise asks the user's keychain for the profile's password on the first start.
            "--use-mock-keychain",
            "--password-store=basic",
            "about:blank",
        ];
        return noSandbox ? flags.Prepend("--no-sandbox") : flags;
    }

    private void Remember(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_log)
        {
            _log.Enqueue(line);
            if (_log.Count > LogLines)
            {
                _log.Dequeue();
            }
        }
    }

    private string Log()
    {
        lock (_log)
        {
            return _log.Count == 0 ? string.Empty : " Chromium's last words: " + string.Join(" | ", _log);
        }
    }

    private void DeleteProfile()
    {
        try
        {
            _profile.Delete(recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A crashed renderer can hold a file a moment longer; the temp folder is the OS's to sweep then.
        }
    }
}
