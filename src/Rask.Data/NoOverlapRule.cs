using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data;

/// <summary>
///     One <c>b.NoOverlap(…)</c>, resolved to the columns of its table: what the provider's DDL is written from, what a
///     refusal is translated by, and what a save is checked against first.
/// </summary>
/// <remarks>
///     Carried on the entity type as a JSON string (<see cref="Annotation" />), so a migration's snapshot keeps it and the
///     next migration can tell what changed. Written by hand, without reflection, so the trimmer has nothing to keep.
/// </remarks>
/// <param name="Name">The constraint's name: PostgreSQL's exclusion constraint, the triggers' stem elsewhere.</param>
/// <param name="Keys">The key's columns.</param>
/// <param name="KeyPaths">The key's properties, as a form names them (<c>Code</c>).</param>
/// <param name="From">The start's column.</param>
/// <param name="FromPath">The start's property (<c>Period.From</c>).</param>
/// <param name="To">The end's column.</param>
/// <param name="ToPath">The end's property (<c>Period.To</c>).</param>
/// <param name="Rows">The provider's SQL for the rows the rule holds — its <c>Where(…)</c> and the soft delete — or empty.</param>
/// <param name="Message">What a refused row is told.</param>
/// <param name="Field">The field it is told under: the range's.</param>
internal sealed record NoOverlapRule(
    string Name,
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> KeyPaths,
    string From,
    string FromPath,
    string To,
    string ToPath,
    string Rows,
    string Message,
    string Field)
{
    /// <summary>The entity-type annotation the resolved rules are carried under.</summary>
    internal const string Annotation = "Rask:NoOverlap";

    /// <summary>The rules <paramref name="entityType" /> carries, or none.</summary>
    internal static IReadOnlyList<NoOverlapRule> Of(IReadOnlyAnnotatable entityType) =>
        entityType.FindAnnotation(Annotation)?.Value is string json ? Parse(json) : [];

    /// <summary>The rules, as the annotation's value.</summary>
    internal static string Write(IReadOnlyList<NoOverlapRule> rules)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var rule in rules)
            {
                json.WriteStartObject();
                json.WriteString("name", rule.Name);
                Strings(json, "keys", rule.Keys);
                Strings(json, "keyPaths", rule.KeyPaths);
                json.WriteString("from", rule.From);
                json.WriteString("fromPath", rule.FromPath);
                json.WriteString("to", rule.To);
                json.WriteString("toPath", rule.ToPath);
                json.WriteString("rows", rule.Rows);
                json.WriteString("message", rule.Message);
                json.WriteString("field", rule.Field);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The rules an annotation's value holds.</summary>
    /// <exception cref="InvalidOperationException">The value is no list of rules.</exception>
    internal static IReadOnlyList<NoOverlapRule> Parse(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return [.. document.RootElement.EnumerateArray().Select(static rule => new NoOverlapRule(
                Text(rule, "name"),
                List(rule, "keys"),
                List(rule, "keyPaths"),
                Text(rule, "from"),
                Text(rule, "fromPath"),
                Text(rule, "to"),
                Text(rule, "toPath"),
                Text(rule, "rows"),
                Text(rule, "message"),
                Text(rule, "field")))];
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                $"The annotation '{Annotation}' holds '{value}', which is no list of NoOverlap rules. Re-create the migration "
                + "that wrote it.", failure);
        }
    }

    /// <summary>Compares two rules by content: <see cref="Keys" /> and <see cref="KeyPaths" /> are lists.</summary>
    public bool Equals(NoOverlapRule? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Keys.SequenceEqual(other.Keys, StringComparer.Ordinal)
        && KeyPaths.SequenceEqual(other.KeyPaths, StringComparer.Ordinal)
        && string.Equals(From, other.From, StringComparison.Ordinal)
        && string.Equals(FromPath, other.FromPath, StringComparison.Ordinal)
        && string.Equals(To, other.To, StringComparison.Ordinal)
        && string.Equals(ToPath, other.ToPath, StringComparison.Ordinal)
        && string.Equals(Rows, other.Rows, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && string.Equals(Field, other.Field, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, From, To, Rows);

    private static void Strings(Utf8JsonWriter json, string name, IReadOnlyList<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    private static string Text(JsonElement rule, string name) => rule.GetProperty(name).GetString() ?? "";

    private static string[] List(JsonElement rule, string name) =>
        [.. rule.GetProperty(name).EnumerateArray().Select(static value => value.GetString() ?? "")];
}
