namespace Rask;

/// <summary>A battery with nothing to configure. On unless the app turns it off.</summary>
public class Battery
{
    /// <summary>Whether this battery will be wired.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Whether the app asked for this battery with <see cref="On"/>, rather than leaving it on by default. A battery that
    /// cannot run on this app's database is skipped when merely defaulted, and refused when asked for.
    /// </summary>
    internal bool TurnedOn { get; private set; }

    /// <summary>Leave this battery out of the app.</summary>
    /// <remarks>
    /// The package stays referenced — this stops the registration, nothing more. Turning it back on is
    /// then a one-line edit rather than a change to the project file, which is the point of referencing
    /// everything.
    /// </remarks>
    public void Off()
    {
        Enabled = false;
        TurnedOn = false;
    }

    /// <summary>
    /// Wire this battery. Every battery is on by default, so this undoes an <see cref="Off"/> — and also records that the
    /// app asked for it: on a database the battery cannot run on (Snapshots on PostgreSQL or SQL Server), a battery
    /// turned on this way refuses the start, where one left on by default is quietly left out.
    /// </summary>
    public void On()
    {
        Enabled = true;
        TurnedOn = true;
    }
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

    /// <summary>Whether the app configured this battery in code at all.</summary>
    internal bool IsConfigured => _configure.Count > 0;

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
