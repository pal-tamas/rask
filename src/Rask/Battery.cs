namespace Rask;

/// <summary>A battery with nothing to configure. On unless the app turns it off.</summary>
public class Battery
{
    /// <summary>Whether this battery will be wired.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>Leave this battery out of the app.</summary>
    /// <remarks>
    /// The package stays referenced — this stops the registration, nothing more. Turning it back on is
    /// then a one-line edit rather than a change to the project file, which is the point of referencing
    /// everything.
    /// </remarks>
    public void Off() => Enabled = false;

    /// <summary>Wire this battery. The default, so this only ever undoes an <see cref="Off"/>.</summary>
    public void On() => Enabled = true;
}

/// <summary>A battery with its own options.</summary>
/// <typeparam name="TOptions">The battery's options type, from its own package.</typeparam>
/// <remarks>
/// <para>
/// Configuration is <b>recorded and replayed</b> rather than held. Each battery's <c>AddRaskX</c> builds
/// its own options instance and hands it to a callback, so what is kept here is the callback — replayed
/// onto the real instance at wiring time. That keeps this type from having to mirror every option a
/// battery has, which is the version that would silently drift the first time one of them gained a
/// property.
/// </para>
/// <para>
/// It also means the options types stay <c>sealed</c>, as everything else in this codebase is.
/// </para>
/// </remarks>
public sealed class Battery<TOptions> : Battery
    where TOptions : class
{
    private readonly List<Action<TOptions>> _configure = [];

    /// <summary>Configures this battery. Call it as often as you like; each call adds to the last.</summary>
    /// <example>
    /// <code>
    /// app.Configure(c => c.Mail.Configure(o => o.From = "no-reply@example.com"));
    /// </code>
    /// </example>
    public Battery<TOptions> Configure(Action<TOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure.Add(configure);
        return this;
    }

    /// <summary>Replays everything recorded onto the battery's real options instance.</summary>
    internal void Apply(TOptions options)
    {
        foreach (var configure in _configure)
        {
            configure(options);
        }
    }
}
