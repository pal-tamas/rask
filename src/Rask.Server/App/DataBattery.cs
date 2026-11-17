using Rask.Server;

namespace Rask;

/// <summary>
/// The database battery: <c>c.Data.Off()</c> like every other, and <see cref="OwnsSchema" /> for a database the app's
/// migrations did not make.
/// </summary>
/// <example>
/// <code>
/// app.Configure(c => c.Data.OwnsSchema(s => s.TakesOverFrom("Init").WhenTable("Users").Patience(10.Minutes)));
/// </code>
/// </example>
public sealed class DataBattery : Battery
{
    internal DataBattery()
    {
    }

    /// <summary>What <see cref="OwnsSchema" /> said, or null when the app never called it.</summary>
    internal SchemaOwnership? Ownership { get; private set; }

    /// <summary>
    /// How the migrations a <c>RaskApp</c> applies as it starts take over a database whose schema predates them, and
    /// how long one may run — the same <see cref="SchemaOwnership" /> a hand-wired host's
    /// <c>AddRaskData&lt;TContext&gt;(…).OwnsSchema(…)</c> takes.
    /// </summary>
    /// <param name="configure">
    /// <c>s =&gt; s.TakesOverFrom("Init").WhenTable("Users").Patience(10.Minutes)</c>. Call it as often as you like; each
    /// call adds to the last.
    /// </param>
    /// <returns>The same battery.</returns>
    /// <remarks>
    /// <para>
    /// It says HOW the migrations apply, not WHETHER: with <c>c.MigrateOnStart = false</c> (or
    /// <c>Rask:Database:MigrateOnStart=false</c>) nothing applies at start, and nothing is taken over either — the
    /// release step that runs <c>rask db update</c> owns the schema then.
    /// </para>
    /// <para>
    /// With the battery off there is no database to own, so <c>c.Data.Off()</c> beside it refuses the start.
    /// </para>
    /// </remarks>
    public DataBattery OwnsSchema(Action<SchemaOwnership> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Ownership ??= new SchemaOwnership();
        configure(Ownership);
        return this;
    }
}
