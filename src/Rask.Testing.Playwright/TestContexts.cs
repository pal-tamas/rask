namespace Rask.Testing;

/// <summary>
///     The browser contexts the site opened for each test, closed once that test is over: before the next test of its
///     lane opens one. A context opened outside a test — a fixture setting itself up — closes with the browser.
/// </summary>
/// <param name="current">The test running on this flow: <see cref="TestScope.Current" />.</param>
internal sealed class TestContexts(Func<TestScope?> current)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<object, (TestScope Scope, Func<Task> Close)> _open = new(ReferenceEqualityComparer.Instance);

    /// <summary>How many contexts are open and not yet closed.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _open.Count;
            }
        }
    }

    /// <summary>Closes every context a test of this lane that has finished left open.</summary>
    /// <returns>A task that completes once they are closed.</returns>
    public Task CloseFinished()
    {
        if (current() is not { } scope)
        {
            return Task.CompletedTask;
        }

        List<Func<Task>> finished = [];
        lock (_gate)
        {
            foreach (var (context, (opener, close)) in _open.ToArray())
            {
                if (string.Equals(opener.Lane, scope.Lane, StringComparison.Ordinal)
                    && !string.Equals(opener.Test, scope.Test, StringComparison.Ordinal))
                {
                    _open.Remove(context);
                    finished.Add(close);
                }
            }
        }

        return finished.Count == 0 ? Task.CompletedTask : Task.WhenAll(finished.Select(close => close()));
    }

    /// <summary>Keeps <paramref name="context" /> for the test running now, to close once it is over.</summary>
    /// <param name="context">The context just opened.</param>
    /// <param name="close">Closes it.</param>
    public void Opened(object context, Func<Task> close)
    {
        if (current() is { } scope)
        {
            lock (_gate)
            {
                _open[context] = (scope, close);
            }
        }
    }

    /// <summary>Forgets <paramref name="context" />: it closed, by the test's hand or the site's.</summary>
    /// <param name="context">The context that closed.</param>
    public void Closed(object context)
    {
        lock (_gate)
        {
            _open.Remove(context);
        }
    }
}
