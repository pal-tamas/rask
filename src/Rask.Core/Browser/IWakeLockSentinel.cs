namespace Rask.Core.Browser;

/// <summary>
///     A held screen wake lock (a <c>WakeLockSentinel</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/WakeLockSentinel" />). Keep the
///     reference for as long as the screen should stay awake, then release it by disposing — ideally with
///     <c>await using</c> or from a component's <c>DisposeAsync</c>.
/// </summary>
public interface IWakeLockSentinel : IAsyncDisposable
{
    // Release is DisposeAsync — the sentinel is the lifetime. Disposing twice is a no-op.
}
