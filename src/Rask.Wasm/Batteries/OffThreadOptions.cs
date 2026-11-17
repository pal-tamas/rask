namespace Rask;

/// <summary>
/// The Web Worker that runs <c>[OffThread]</c> queries and commands beside the page:
/// <c>host.Configure(c => c.OffThread.Warm = true)</c>.
/// </summary>
/// <remarks>
/// The worker is a second .NET runtime, made from the app's own <c>_framework</c> files, so it costs nothing to
/// download that the page has not already downloaded — but it costs its memory, and the time to start it, which by
/// default is spent the first time an <c>[OffThread]</c> message is sent. An app that has none never starts it.
/// </remarks>
public sealed class OffThreadOptions
{
    /// <summary>
    /// Starts the worker once the first page is on screen, rather than at the first <c>[OffThread]</c> message, so that
    /// one is answered without waiting for a runtime to start. Default <see langword="false"/>.
    /// </summary>
    public bool Warm { get; set; }
}
