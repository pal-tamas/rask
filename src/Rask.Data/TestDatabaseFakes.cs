using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     The batteries' fakes a <see cref="TestDatabase" /> stands in for the real batteries with — so a save handler that
///     sends mail, enqueues a job or caches a value records it instead of throwing for want of a host.
/// </summary>
/// <remarks>
///     Each battery's own assembly makes its fake (Rask.Data references none of them) and hands it here, through
///     <see cref="ITestDatabaseBattery" />. A fake the test made before starting the database is the one used, and
///     stays the test's; one the database made is the database's, let go of when it is disposed. A battery the test
///     registered itself through the services hook wins: its fake is let go of at once.
/// </remarks>
internal sealed class TestDatabaseFakes
{
    private readonly List<Held> _held = [];

    /// <summary>
    ///     <paramref name="already" /> — the fake the test made in this flow — or one made now, registered into
    ///     <paramref name="services" /> as <typeparamref name="TService" />.
    /// </summary>
    /// <param name="services">The services the database's saves publish through.</param>
    /// <param name="already">The fake the test made itself, or <see langword="null" />.</param>
    /// <param name="make">Makes the database's own; it takes the battery's place in the calling flow.</param>
    public TFake Use<TService, TFake>(IServiceCollection services, TFake? already, Func<TFake> make)
        where TService : class
        where TFake : class, TService, IDisposable
    {
        ArgumentNullException.ThrowIfNull(make);
        var fake = already ?? make();
        services.AddSingleton<TService>(fake);
        _held.Add(new Held(typeof(TService), fake, Made: already is null));
        return fake;
    }

    /// <summary>The fake of type <typeparamref name="TFake" /> this database stands in with, or <see langword="null" />.</summary>
    public TFake? Find<TFake>()
        where TFake : class =>
        _held.Select(static h => h.Fake).OfType<TFake>().FirstOrDefault();

    /// <summary>The fake of type <typeparamref name="TFake" />, or the error that says why there is none.</summary>
    /// <param name="battery">The battery, as a sentence names it: <c>mail</c>.</param>
    /// <param name="mapping">The line that maps its table: <c>modelBuilder.AddRaskMail()</c>.</param>
    /// <param name="fake">The line that makes one by hand: <c>Mail.Fake()</c>.</param>
    /// <exception cref="InvalidOperationException">The database stands in no such fake.</exception>
    public TFake Required<TFake>(string battery, string mapping, string fake)
        where TFake : class =>
        Find<TFake>() ?? throw new InvalidOperationException(
            $"This test database has no {battery} fake: its model maps no {battery} table, or the test registered a {battery} "
            + $"battery of its own through the services hook. Map the table ({mapping}) — a RaskApp's context maps it — or "
            + $"make the fake yourself: using var fake = {fake};");

    /// <summary>
    ///     Lets go, at once, of every fake whose service the test registered a stand-in of its own for — the last
    ///     registration is the one a handler is handed, and the fake must not shadow it for the static facade either.
    /// </summary>
    public void Settle(IServiceCollection services)
    {
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            var held = _held[i];
            var last = services.LastOrDefault(d => d.ServiceType == held.Service && !d.IsKeyedService);
            if (!ReferenceEquals(last?.ImplementationInstance, held.Fake))
            {
                Release(held);
                _held.RemoveAt(i);
            }
        }
    }

    /// <summary>Lets go of every fake, the last made first — so each puts back what was in place before it.</summary>
    /// <remarks>Not async, so it restores the flow that disposes the database: the test's.</remarks>
    public void Release()
    {
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            Release(_held[i]);
        }

        _held.Clear();
    }

    private static void Release(Held held)
    {
        if (held.Made)
        {
            held.Fake.Dispose();
        }
    }

    private sealed record Held(Type Service, IDisposable Fake, bool Made);
}
