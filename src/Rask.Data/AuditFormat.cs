using System.Globalization;
using System.Text;

namespace Rask.Data;

/// <summary>
///     How an <see cref="AuditEntry" /> writes the values it records: <c>Number=17 || Km.Start=1200</c> — each
///     <c>Name=value</c>, joined by <c> || </c>, a null left out, every value in the invariant culture.
/// </summary>
/// <remarks>
///     Replaced by registering one of the app's own, for a table whose old rows were written another way:
///     <c>services.AddSingleton&lt;AuditFormat, LegacyAuditFormat&gt;();</c> — overriding <see cref="Values" /> for the
///     whole text, or <see cref="Value" /> for one value's.
/// </remarks>
public class AuditFormat
{
    private const string Separator = " || ";

    /// <summary>The format Rask writes with unless the app registers its own.</summary>
    public static AuditFormat Default { get; } = new();

    /// <summary>The text of <paramref name="values" />: <c>Name=value || Name=value</c>, the nulls left out.</summary>
    /// <param name="values">What is recorded, in the order of the aggregate's columns, then its rows'.</param>
    /// <returns>The text; empty when every value is null.</returns>
    public virtual string Values(IReadOnlyList<AuditValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var text = new StringBuilder();
        foreach (var (name, value) in values)
        {
            if (value is null)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(Separator);
            }

            text.Append(name).Append('=').Append(Value(value));
        }

        return text.ToString();
    }

    /// <summary>
    ///     One value's text, in the invariant culture: a decimal without trailing zeros (<c>1.5</c>, whatever scale the
    ///     column keeps), a moment round-tripping (<c>2026-11-24T13:05:00.0000000Z</c>).
    /// </summary>
    /// <param name="value">The value as the database stores it; never null.</param>
    /// <returns>Its text.</returns>
    public virtual string Value(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
            DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
    }

    /// <summary>The type of the row the format writes: <see cref="AuditEntry" />, or the app's own.</summary>
    internal virtual Type RowType => typeof(AuditEntry);

    /// <summary>Whether the format is handed every value the aggregate held and holds, not only the ones that changed.</summary>
    internal virtual bool ReadsSnapshot => false;

    /// <summary>Who the save is for, read once per save: the id claim as text, <see cref="AuditEntry.System" /> for nobody.</summary>
    internal virtual object? User() => CurrentUser.Id is { } id ? CurrentUser.ToClaim(id) : AuditEntry.System;

    /// <summary>The row that records <paramref name="draft" />.</summary>
    /// <param name="aggregate">The aggregate's type.</param>
    /// <param name="draft">What the save did to it.</param>
    /// <param name="user">What <see cref="User" /> answered for the save.</param>
    /// <param name="at">When the save was made, in UTC.</param>
    internal virtual object RowOf(Type aggregate, AuditDraft draft, object? user, DateTime at) =>
        new AuditEntry(
            aggregate.Name,
            draft.Action,
            Values(draft.Key),
            draft.Action == AuditAction.Insert ? null : Values(draft.Before),
            draft.Action == AuditAction.Delete ? null : Values(draft.After),
            (string)user!,
            at);
}
