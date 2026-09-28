using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>A searched row paired with its rank, between the join and the projection back to the row.</summary>
/// <typeparam name="TEntity">The searched entity.</typeparam>
internal sealed class FullTextHit<TEntity>
{
    public TEntity Item { get; set; } = default!;

    public double Rank { get; set; }
}
