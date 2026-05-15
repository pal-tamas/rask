namespace Rask.Core.Forms;

internal static class DispatchServicesScope
{
    private static readonly AsyncLocal<IServiceProvider?> _current = new();

    public static IServiceProvider? Current => _current.Value;

    public static IDisposable Push(IServiceProvider? services)
    {
        var prev = _current.Value;
        _current.Value = services;
        return new Popper(prev);
    }

    private sealed class Popper : IDisposable
    {
        private readonly IServiceProvider? _prev;
        public Popper(IServiceProvider? prev) => _prev = prev;
        public void Dispose() => _current.Value = _prev;
    }
}
