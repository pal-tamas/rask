using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>A declared numbering, as a save keeps it: what the save touched, and the groups numbered anew.</summary>
internal interface INumbering
{
    /// <summary>The numbered entity.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    Type Entity { get; }

    /// <summary>The numbered property.</summary>
    string NumberName { get; }

    /// <summary><c>Entity.Number</c>: what tells two numberings apart.</summary>
    string Name { get; }

    /// <summary>The groups <paramref name="context" />'s save about to run touches, each from the earliest place it touches.</summary>
    Task<NumberingTouch> Touched(DbContext context, CancellationToken cancellationToken);

    /// <summary>Numbers the touched groups anew, once the save's rows went out.</summary>
    Task Renumber(DbContext context, NumberingTouch touched, CancellationToken cancellationToken);

    /// <summary>
    ///     Numbers <paramref name="groups" /> anew from their first row — every group, and the rows in none, when null — under
    ///     their locks in <paramref name="context" />'s transaction.
    /// </summary>
    /// <returns>How many rows' numbers changed.</returns>
    Task<int> Mend(DbContext context, IReadOnlyCollection<object>? groups, CancellationToken cancellationToken);
}
