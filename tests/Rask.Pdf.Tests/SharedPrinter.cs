namespace Rask.Printing.Tests;

/// <summary>One browser for a class's tests, as an app keeps one: started at the first print, closed after the last.</summary>
public sealed class SharedPrinter : IAsyncDisposable
{
    private readonly Lazy<ChromiumPrinter> _printer = new(() => TestChromium.Printer());

    internal ChromiumPrinter Printer => _printer.Value;

    public async ValueTask DisposeAsync()
    {
        if (_printer.IsValueCreated)
        {
            await _printer.Value.DisposeAsync();
        }
    }
}
