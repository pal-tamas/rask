using Spectre.Console;

namespace Rask.Cli;

/// <summary>The real console, wired in <c>Program.cs</c>.</summary>
internal sealed class SystemConsole : IConsole
{
    // Built once and cached: constructing a renderer probes the terminal, and both surfaces must keep
    // the same profile for the life of the process so widths don't shift mid-command.
    //
    // A prompt needs a terminal on *both* ends — keys come from stdin, the list it redraws goes to
    // stdout — so `rask new > log` with a live keyboard is correctly not interactive.
    private readonly Lazy<IAnsiConsole> _ansi = new(() => AnsiConsoleFactory.Create(
        Console.Out,
        ansi: !Console.IsOutputRedirected,
        color: ConsoleStyling.ColorEnabled(Console.IsOutputRedirected),
        interactive: !Console.IsOutputRedirected && !Console.IsInputRedirected));

    private readonly Lazy<IAnsiConsole> _ansiError = new(() => AnsiConsoleFactory.Create(
        Console.Error,
        ansi: !Console.IsErrorRedirected,
        color: ConsoleStyling.ColorEnabled(Console.IsErrorRedirected),
        interactive: false));

    public static SystemConsole Instance { get; } = new();

    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public TextReader In => Console.In;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool IsInputRedirected => Console.IsInputRedirected;

    public IAnsiConsole Ansi => _ansi.Value;

    public IAnsiConsole AnsiError => _ansiError.Value;
}
