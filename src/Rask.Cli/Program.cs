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
finally
{
    Console.CancelKeyPress -= OnCancel;
}
