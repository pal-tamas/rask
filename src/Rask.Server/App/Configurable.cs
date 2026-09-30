namespace Rask;

/// <summary>
///     A part of the host that is always there and can be tuned: <c>c.Live.Configure(o =&gt; o.MaxSessions = 5_000)</c>.
///     Every call is kept and applied in order, so two places that tune it both take effect.
/// </summary>
/// <remarks>
///     A battery (<see cref="Battery{TOptions}" />) without the switch — the live runtime and the server limits are
///     the host itself, so there is nothing to turn off.
/// </remarks>
/// <typeparam name="TOptions">The options it is tuned through.</typeparam>
public sealed class Configurable<TOptions>
    where TOptions : class
{
    private readonly List<Action<TOptions>> _configure = [];

    /// <summary>Adds a change to the options; earlier ones still apply.</summary>
    /// <param name="configure">The change.</param>
    /// <returns>This, to tune it again.</returns>
    public Configurable<TOptions> Configure(Action<TOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure.Add(configure);
        return this;
    }

    internal void Apply(TOptions options)
    {
        foreach (var configure in _configure)
        {
            configure(options);
        }
    }
}
