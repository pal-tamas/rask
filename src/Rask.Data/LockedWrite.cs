using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Rask.Data;

/// <summary>A row a save writes that a lock is asked about, and where it sits in its aggregate's form: <c>""</c>, <c>"Lines[2]."</c>.</summary>
/// <param name="Entry">The row.</param>
/// <param name="Path">Its place, as <see cref="RowPaths" /> names it.</param>
internal readonly record struct LockedWrite(EntityEntry Entry, string Path);
