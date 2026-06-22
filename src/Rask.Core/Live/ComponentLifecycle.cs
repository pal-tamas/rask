namespace Rask.Core.Live;

internal static class ComponentLifecycle
{
    internal static void DisposeComponentTree(Component component)
    {
        // Run the teardown at most once per component — a tree mutation inside an OnUnmount
        // hook could otherwise route the same node through a second dispose pass.
        if (!component.TryBeginDispose())
        {
            return;
        }

        foreach (var child in component.PersistedChildren.Values)
        {
            DisposeComponentTree(child);
        }

        // OnUnmount fires before token cancellation so the hook can still observe a live
        // token. Any user CancellationToken.Register callbacks then fire on CancelLifetimeToken
        // immediately below — both mechanisms work, additive.
        var unmountTask = component.RaiseUnmount();
        if (unmountTask is not null)
        {
            _ = ObserveUnmountFault(unmountTask, component);
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
        // Run the teardown at most once per component — a tree mutation inside an OnUnmount
        // hook could otherwise route the same node through a second dispose pass.
        if (!component.TryBeginDispose())
        {
            return;
        }

        foreach (var child in component.PersistedChildren.Values)
        {
            await DisposeComponentTreeAsync(child).ConfigureAwait(false);
        }

        var unmountTask = component.RaiseUnmount();
        if (unmountTask is not null)
        {
            try { await unmountTask.ConfigureAwait(false); }
            catch (Exception ex) { Component.LogUnmountError(component, ex); }
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

    private static async Task ObserveUnmountFault(Task task, Component component)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception ex) { Component.LogUnmountError(component, ex); }
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
