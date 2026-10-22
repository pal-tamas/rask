namespace Rask;

/// <summary>
///     Services that start the first time a call cannot do without them: a test's own app, which Rask.Testing opens
///     around every test and boots only when the test needs it.
/// </summary>
/// <remarks>
///     Made ambient like any work's services, but a lookup that has another answer — an <c>IsOn</c> check, an
///     optional service, a database the test configured itself — never starts them (<see cref="Ambient.Services" />);
///     only a call that would otherwise fail does (<see cref="Ambient.Needed" />). Once the test sets up a database of
///     its own (<see cref="Decline" />) nothing starts them at all, so referencing Rask.Testing changes nothing a
///     test did not ask for.
/// </remarks>
internal abstract class OnDemandServices : IServiceProvider
{
    private volatile bool _declined;

    /// <summary>The services, once started; <c>null</c> before.</summary>
    internal abstract IServiceProvider? Started { get; }

    /// <summary>The services, started now if they were not — or <c>null</c> once declined, or with nothing to start.</summary>
    internal IServiceProvider? Start() => Started ?? (_declined ? null : StartNow());

    /// <summary>The test set up what it needs itself: nothing starts these on its behalf from here on.</summary>
    internal void Decline() => _declined = true;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => Start()?.GetService(serviceType);

    /// <summary>Starts the services; <c>null</c> when there is nothing to start.</summary>
    protected abstract IServiceProvider? StartNow();
}
