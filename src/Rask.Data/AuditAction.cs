namespace Rask.Data;

/// <summary>What a save did to an <see cref="Audited" /> aggregate, as its <see cref="AuditEntry" /> records it.</summary>
public enum AuditAction
{
    /// <summary>The aggregate was created.</summary>
    Insert,

    /// <summary>The aggregate, or a row it holds, was changed.</summary>
    Update,

    /// <summary>The aggregate was deleted, or soft-deleted.</summary>
    Delete,
}
