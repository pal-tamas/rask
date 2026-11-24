using System.Text.Json;

namespace Rask.Data;

/// <summary>
///     A table being named by <c>TableNames</c>: the class it holds, and the names Rask can already give it —
///     <c>c.TableNames(t =&gt; t.Class)</c>.
/// </summary>
public sealed class TableName
{
    internal TableName(string @class, string asDeclared)
    {
        Class = @class;
        AsDeclared = asDeclared;
    }

    /// <summary>The entity's class name, as it is written: <c>Fueling</c>.</summary>
    public string Class { get; }

    /// <summary>What EF Core names it: the context's <c>DbSet</c> property for it (<c>Fuelings</c>), or else <see cref="Class" />.</summary>
    public string AsDeclared { get; }

    /// <summary>The class name in camel case: <c>fueling</c>, <c>deliveryNote</c>.</summary>
    public string CamelCase => JsonNamingPolicy.CamelCase.ConvertName(Class);

    /// <summary>The class name in snake case: <c>fueling</c>, <c>delivery_note</c>.</summary>
    public string SnakeCase => JsonNamingPolicy.SnakeCaseLower.ConvertName(Class);
}
