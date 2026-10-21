using System.Globalization;

namespace Rask.Printing;

/// <summary>
/// The app's one Chromium, started at the first print and kept: a bounded number of documents print at once, a
/// browser that crashed is replaced at the next print, and disposing (with the host) closes it.
/// </summary>
internal sealed class ChromiumPrinter(ChromiumSettings settings) : IAsyncDisposable
{
    private readonly SemaphoreSlim _turns = new(settings.MaxConcurrentPrints, settings.MaxConcurrentPrints);
    private readonly SemaphoreSlim _starting = new(1, 1);
    private ChromiumProcess? _browser;
    private int _printing;
    private int _mostAtOnce;
    private volatile bool _disposed;

    /// <summary>The most documents that were printing at the same moment.</summary>
    internal int MostAtOnce => Volatile.Read(ref _mostAtOnce);

    /// <summary>The browser printing now, if one has started.</summary>
    internal ChromiumProcess? Browser => _browser;

    /// <summary>Prints <paramref name="document"/> and returns the PDF's bytes.</summary>
    /// <exception cref="TimeoutException">The document took longer than <see cref="ChromiumSettings.Timeout"/>.</exception>
    public async Task<byte[]> Print(PrintDocument document, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _turns.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            CountIn();
            var browser = await Running(cancellation).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(settings.Timeout);
            try
            {
                return await HtmlPrint.Run(browser, document, settings.MaxPages, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) when (!cancellation.IsCancellationRequested)
            {
                throw new TimeoutException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The PDF was not ready within {settings.Timeout.TotalSeconds:0.#} seconds (Rask:Pdf:Timeout)."),
                    e);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _printing);
            _turns.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _starting.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync().ConfigureAwait(false);
                _browser = null;
            }
        }
        finally
        {
            _starting.Release();
        }
    }

    private async Task<ChromiumProcess> Running(CancellationToken cancellation)
    {
        if (_browser is { IsGone: false } running)
        {
            return running;
        }

        await _starting.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_browser is { IsGone: false } started)
            {
                return started;
            }

            if (_browser is not null)
            {
                // It crashed or was killed: reap it and drop its profile before the next one starts.
                await _browser.DisposeAsync().ConfigureAwait(false);
            }

            _browser = ChromiumProcess.Start(ChromiumLocator.Find(settings.ChromiumPath), settings.NoSandbox);
            return _browser;
        }
        finally
        {
            _starting.Release();
        }
    }

    private void CountIn()
    {
        var now = Interlocked.Increment(ref _printing);
        var most = Volatile.Read(ref _mostAtOnce);
        while (now > most)
        {
            var seen = Interlocked.CompareExchange(ref _mostAtOnce, now, most);
            if (seen == most)
            {
                return;
            }

            most = seen;
        }
    }
}
