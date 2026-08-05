namespace Rask.Dashboard.Pages;

/// <summary>
/// A panel that re-reads itself on a timer while it is on screen.
/// <para>
/// Four things here are load-bearing, and all four exist because the dashboard reads the same database the
/// processors are writing to:
/// </para>
/// <list type="number">
///   <item>
///     <b>The loop is fire-and-forget.</b> <see cref="OnMountAsync" /> awaits only the first read; awaiting
///     the loop itself would never return and the page would never mount.
///   </item>
///   <item>
///     <b>Compare before re-rendering.</b> <see cref="LoadAsync" /> returns a value compared with the
///     previous one, so an unchanged reading produces no <c>StateHasChanged</c> — an idle system generates
///     no diff and no WebSocket traffic at all.
///   </item>
///   <item>
///     <b>The loop is bounded.</b> After <see cref="RaskDashboardOptions.MaxPollDuration" /> the panel parks
///     and offers a Resume button. Every open tab is a reader competing for the write lock, and a dashboard
///     left open on a wall display would otherwise poll forever.
///   </item>
///   <item>
///     <b><c>ConfigureAwait(false)</c> throughout.</b> Staying off the lifecycle synchronization context
///     keeps the loop from triggering a render per await.
///   </item>
/// </list>
/// </summary>
public abstract class PollingPanel : Component
{
    private object? _previous;
    private bool _running;

    /// <summary>The dashboard options. Inject them and return them.</summary>
    protected abstract RaskDashboardOptions Options { get; }

    /// <summary>
    /// Reads the panel's data into fields and returns a value used purely for change detection — return a
    /// value type or record whose equality means "the screen would look the same".
    /// </summary>
    protected abstract Task<object?> LoadAsync(CancellationToken cancellationToken);

    /// <summary><c>true</c> once the loop has stopped and the Resume affordance should show.</summary>
    protected bool IsParked { get; private set; }

    /// <summary><c>true</c> until the first read completes, so the panel can show a placeholder.</summary>
    protected bool IsLoading { get; private set; } = true;

    /// <summary>The most recent read that failed, if the last one did.</summary>
    protected string? LoadError { get; private set; }

    /// <inheritdoc />
    protected override async Task OnMountAsync()
    {
        // Captured here, in a lifecycle hook, where CancellationToken is the component's LIFETIME token —
        // read inside a handler dispatch it would also carry that dispatch's timeout, which would kill the
        // loop early. It is cancelled when the component leaves the tree, so no teardown code is needed.
        var lifetime = CancellationToken;

        await ReadAsync(lifetime).ConfigureAwait(false);
        IsLoading = false;
        Start(lifetime);
    }

    /// <summary>Restarts the loop after it parked.</summary>
    protected Task ResumeAsync()
    {
        if (IsParked)
        {
            IsParked = false;
            Start(CancellationToken);
        }

        return Task.CompletedTask;
    }

    private void Start(CancellationToken cancellationToken)
    {
        if (_running || Options.RefreshInterval <= TimeSpan.Zero)
        {
            return;
        }

        _running = true;
        _ = PollAsync(cancellationToken);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var deadline = Options.MaxPollDuration > TimeSpan.Zero
            ? DateTimeOffset.UtcNow + Options.MaxPollDuration
            : DateTimeOffset.MaxValue;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Options.RefreshInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!await ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return; // cancelled mid-read: the component is gone
                }

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    IsParked = true;
                    StateHasChanged();
                    return;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    // Returns false only when the component is going away. A query that throws is shown in the panel
    // rather than silently ending the loop: a dashboard that quietly stops updating is worse than one
    // that says it couldn't read.
    private async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        object? current;
        string? error;
        try
        {
            current = await LoadAsync(cancellationToken).ConfigureAwait(false);
            error = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
#pragma warning disable CA1031 // A failed read must not tear the panel down — surface it and keep polling.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            current = null;
            error = ex.Message;
        }

        // The error is part of what's on screen, so it joins the comparison — otherwise a database that
        // stays down would re-render identical content on every tick. Equals(null, null) is true, so a
        // panel whose battery isn't present settles after the first read and then stays silent.
        var reading = (current, error);
        if (Equals(_previous, reading))
        {
            return true;
        }

        _previous = reading;
        LoadError = error;
        StateHasChanged();
        return true;
    }
}
