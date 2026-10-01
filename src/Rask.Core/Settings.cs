namespace Rask;

/// <summary>Settings for a part of the app that is always on: it is configured, never turned off.</summary>
/// <typeparam name="TOptions">The part's options type, from its own package.</typeparam>
/// <remarks>
/// Recorded and replayed like a <see cref="Battery{TOptions}"/>'s: what is kept is the callback, applied to the real
/// options instance at wiring time, so this type never mirrors an option the package adds later.
/// </remarks>
public sealed class Settings<TOptions>
    where TOptions : class
{
    private readonly List<Action<TOptions>> _configure = [];

    /// <summary>Whether the app configured these settings in code at all.</summary>
    internal bool IsConfigured => _configure.Count > 0;

    /// <summary>Configures it. Call it as often as you like; each call adds to the last.</summary>
    /// <example>
    /// <code>
    /// app.Configure(c => c.Outbox.Configure(o => o.PollInterval = 1.Second));
    /// </code>
    /// </example>
    public Settings<TOptions> Configure(Action<TOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure.Add(configure);
        return this;
    }

    /// <summary>Replays everything recorded onto the real options instance.</summary>
    internal void Apply(TOptions options)
    {
        foreach (var configure in _configure)
        {
            configure(options);
        }
    }
}
