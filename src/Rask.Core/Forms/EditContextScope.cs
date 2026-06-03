namespace Rask.Core.Forms;

public static class EditContextScope
{
    private static readonly AsyncLocal<EditContext?> _current = new();

    public static EditContext? Current => _current.Value;

    internal static IDisposable Push(EditContext ctx)
    {
        var prev = _current.Value;
        _current.Value = ctx;
        return new Popper(prev);
    }

    private sealed class Popper(EditContext? prev) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current.Value = prev;
        }
    }
}
