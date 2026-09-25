namespace Rask.Core.Browser;

/// <summary>How a <see cref="IWebLocks" /> lock is held — see the Web Locks API's <c>mode</c> option.</summary>
public enum LockMode
{
    /// <summary>Only one holder at a time (the default). Waits for any current holder to release.</summary>
    Exclusive,

    /// <summary>Any number of shared holders concurrently, but never alongside an exclusive holder.</summary>
    Shared,
}
