namespace Rask.Cli;

/// <summary>
/// The tool's console output seam. Abstracting <see cref="System.Console"/> keeps every command
/// unit-testable — tests substitute an in-memory writer and assert on the captured text.
/// </summary>
internal interface IConsole
{
    TextWriter Out { get; }

    TextWriter Error { get; }
}

/// <summary>The real console, wired in <c>Program.cs</c>.</summary>
internal sealed class SystemConsole : IConsole
{
    public static SystemConsole Instance { get; } = new();

    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;
}
