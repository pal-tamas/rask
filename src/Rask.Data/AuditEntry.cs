namespace Rask.Data;

/// <summary>
///     One save of an <see cref="Audited" /> aggregate, written in that same save: which aggregate, what was done, what it
///     was and became, by whom and when.
/// </summary>
/// <remarks>
///     <para>
///         The table is <c>Audits</c>, mapped in every model that has an <see cref="Audited" /> aggregate; its names stay
///         as they are under the app's naming conventions. An app can say otherwise in its <c>IRaskModel</c>:
///         <c>b.Entity&lt;AuditEntry&gt;().Property(a =&gt; a.Aggregate).HasColumnName("TableName")</c>.
///     </para>
///     <para>
///         <see cref="Key" />, <see cref="Before" /> and <see cref="After" /> are written by the app's
///         <see cref="AuditFormat" />: by default <c>Name=value || Name=value</c>, in the invariant culture, with a null
///         left out.
///     </para>
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>Who <see cref="User" /> is for work that is no signed-in user's: a job, a hosted service, startup.</summary>
    public const string System = "system";

    internal AuditEntry(string aggregate, AuditAction action, string key, string? before, string? after, string user, DateTime at)
    {
        Aggregate = aggregate;
        Action = action;
        Key = key;
        Before = before;
        After = after;
        User = user;
        At = at;
    }

    private AuditEntry()
    {
    } // EF materialization

    /// <summary>The entry's own number.</summary>
    public long Id { get; }

    /// <summary>The aggregate's type name: <c>DeliveryNote</c>.</summary>
    public string Aggregate { get; private set; } = "";

    /// <summary>What the save did to it.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>The aggregate's key, formatted: <c>Id=42</c>.</summary>
    public string Key { get; internal set; } = "";

    /// <summary>What changed, as it was; null for an <see cref="AuditAction.Insert" />.</summary>
    public string? Before { get; private set; }

    /// <summary>What changed, as it became; null for an <see cref="AuditAction.Delete" />.</summary>
    public string? After { get; internal set; }

    /// <summary>The signed-in user the work was for, as their id claim reads; <see cref="System" /> for none.</summary>
    public string User { get; private set; } = System;

    /// <summary>When the save was made, in UTC.</summary>
    public DateTime At { get; private set; }
}
