namespace Rask.Core;

// How Rask.Testing's Page.Visit runs an app as written: it runs the app's own entry point while a capture is open, and
// the host — AddRask, which every Rask server app calls, seeing the capture — serves nothing and, once started, hands
// its services back here along with a way to stop it. The app's Program.cs is the composition under test, not a copy.
internal static class AppCapture
{
    private static readonly AsyncLocal<Capture?> Open = new();

    /// <summary>The capture the entry point running in this flow reports to, or <c>null</c> outside a test.</summary>
    internal static Capture? Current => Open.Value;

    /// <summary>Opens a capture for an entry point about to run in this flow.</summary>
    internal static Capture Begin()
    {
        var capture = new Capture();
        Open.Value = capture;
        return capture;
    }

    /// <summary>Closes the capture this flow opened; an entry point already started keeps reporting to it.</summary>
    internal static void End() => Open.Value = null;

    internal sealed class Capture
    {
        private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The started app's services, once handed over.</summary>
        public IServiceProvider? Services { get; private set; }

        /// <summary>Stops the app the way a shutdown signal does, so its own Run returns and disposes it.</summary>
        public Action? Stop { get; private set; }

        /// <summary>What the entry point threw before it handed anything over.</summary>
        public Exception? Error { get; private set; }

        /// <summary>The host calls this once the app has started, instead of anything reaching it over a socket.</summary>
        internal void Hand(IServiceProvider services, Action stop)
        {
            Services = services;
            Stop = stop;
            _settled.TrySetResult();
        }

        /// <summary>The entry point ended — having handed an app over, or not.</summary>
        internal void Exited(Exception? error = null)
        {
            Error ??= error;
            _settled.TrySetResult();
        }

        /// <summary>Waits until the app is handed over or the entry point has ended.</summary>
        internal bool Wait(TimeSpan timeout) => _settled.Task.Wait(timeout);
    }
}
