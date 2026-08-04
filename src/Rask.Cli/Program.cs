using Rask.Cli;
using Rask.Cli.Scaffolding;

var app = CliApplication.CreateDefault(SystemConsole.Instance, new ProcessRunner(), new SystemFileSystem());

using var cts = new CancellationTokenSource();

void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs)
{
    // First Ctrl+C: ask the child to stop gracefully. Let a second one hard-kill the tool.
    eventArgs.Cancel = true;
    try
    {
        cts.Cancel();
    }
    catch (ObjectDisposedException)
    {
        // The run already finished and disposed the source — nothing left to cancel.
    }
}

Console.CancelKeyPress += OnCancel;

try
{
    return await app.RunAsync(args, cts.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C: exit cleanly with the conventional SIGINT code, not an unhandled-exception stack trace.
    return 130;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
{
    // The filesystem said no — a read-only directory, a full disk, a file held open by an editor. The
    // scaffolder writes through raw File/Directory calls, so before this these surfaced as a .NET stack
    // trace: alarming, and it buried the one line that says which path failed.
    SystemConsole.Instance.WriteErrorLine(exception.Message, ConsoleStyle.Error);
    SystemConsole.Instance.Error.WriteLine("Check the path is writable and not open elsewhere. Set RASK_DEBUG=1 for the full stack trace.");
    if (Environment.GetEnvironmentVariable("RASK_DEBUG") == "1")
    {
        SystemConsole.Instance.Error.WriteLine(exception.ToString());
    }

    return 1;
}
finally
{
    Console.CancelKeyPress -= OnCancel;
}
