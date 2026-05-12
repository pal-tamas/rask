namespace Rask.Core.Live;

internal static class ComponentLifecycle
{
    internal static void DisposeComponentTree(Component component)
    {
        foreach (var child in component.PersistedChildren.Values)
        {
            DisposeComponentTree(child);
        }

        component.CancelLifetimeToken();

        switch (component)
        {
            case IAsyncDisposable ad:
                // CTS disposal rides along inside DisposeAsyncSafe so the user's async dispose
                // body can still observe the (already-cancelled) token before it is torn down.
                _ = DisposeAsyncSafe(ad, component);
                return;
            case IDisposable d:
                try { d.Dispose(); }
                catch (Exception ex) { LogDisposeError(component, ex); }

                break;
        }

        component.DisposeLifetimeToken();
    }

    internal static async Task DisposeComponentTreeAsync(Component component)
    {
        foreach (var child in component.PersistedChildren.Values)
        {
            await DisposeComponentTreeAsync(child).ConfigureAwait(false);
        }

        component.CancelLifetimeToken();

        switch (component)
        {
            case IAsyncDisposable ad:
                try { await ad.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogDisposeError(component, ex); }

                break;
            case IDisposable d:
                try { d.Dispose(); }
                catch (Exception ex) { LogDisposeError(component, ex); }

                break;
        }

        component.DisposeLifetimeToken();
    }

    private static async Task DisposeAsyncSafe(IAsyncDisposable ad, Component component)
    {
        try { await ad.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { LogDisposeError(component, ex); }
        component.DisposeLifetimeToken();
    }

    private static void LogDisposeError(Component component, Exception ex) =>
        Console.Error.WriteLine($"Rask component dispose on {component.GetType().Name} threw: {ex}");
}
