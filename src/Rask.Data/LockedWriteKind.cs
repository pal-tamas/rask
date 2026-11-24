namespace Rask.Data;

/// <summary>What a write a declared lock refuses does to its row (<see cref="LockedWrite{T}" />).</summary>
public enum LockedWriteKind
{
    /// <summary>The save adds the row.</summary>
    Add,

    /// <summary>The save changes the row, or a child of it, and leaves its parent as it was.</summary>
    Change,

    /// <summary>The save deletes the row.</summary>
    Delete,

    /// <summary>The save moves the row to another parent, or gives it one.</summary>
    Move,
}
