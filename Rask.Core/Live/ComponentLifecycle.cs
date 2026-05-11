namespace Rask.Core.Live;

internal static class ComponentLifecycle
{
    internal static void DisposeComponentTree(Component component)
    {
        foreach (var child in component.PersistedChildren.Values)
        {
            DisposeComponentTree(child);
        }

        switch (component)
        {
            case IAsyncDisposable ad:
                _ = DisposeAsyncSafe(ad, component);
                break;
            case IDisposable d:
                try { d.Dispose(); }
                catch (Exception ex) { LogDisposeError(component, ex); }

                break;
        }
    }

    internal static async Task DisposeComponentTreeAsync(Component component)
    {
        foreach (var child in component.PersistedChildren.Values)
        {
            await DisposeComponentTreeAsync(child).ConfigureAwait(false);
        }

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
    }

    private static async Task DisposeAsyncSafe(IAsyncDisposable ad, Component component)
    {
        try { await ad.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { LogDisposeError(component, ex); }
    }

    private static void LogDisposeError(Component component, Exception ex) =>
        Console.Error.WriteLine($"Rask component dispose on {component.GetType().Name} threw: {ex}");
}
