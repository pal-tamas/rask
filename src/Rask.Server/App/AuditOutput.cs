using System.Globalization;
using System.Text;
using System.Text.Json;
using Rask.Data;

namespace Rask;

/// <summary>
///     How <c>rask audit</c> says what it found: each aggregate with the rows read, under it each rule broken with the
///     field, the count and the ids — as text for a person, or as JSON (<c>--json</c>) for a script, which lists every id.
/// </summary>
internal static class AuditOutput
{
    // Ids listed per rule in the text; the JSON lists them all.
    private const int Shown = 50;

    /// <summary>The report for a person to read.</summary>
    internal static string Text(RuleAuditReport report)
    {
        var text = new StringBuilder();
        var rows = report.Read.Sum(static r => r.Rows);
        text.Append(CultureInfo.InvariantCulture, $"rask audit: {report.Read.Count} aggregate(s), {rows} row(s) read, ")
            .Append(report.Clean ? "every one passes its rules." : $"{report.Breaches.Count} rule(s) broken.")
            .AppendLine();

        foreach (var aggregate in report.Read)
        {
            var broken = report.Breaches.Where(b => string.Equals(b.Aggregate, aggregate.Aggregate, StringComparison.Ordinal)).ToList();
            text.AppendLine()
                .Append(CultureInfo.InvariantCulture, $"{aggregate.Aggregate} — {aggregate.Rows} read")
                .AppendLine(broken.Count == 0 ? ", all pass." : ":");
            Breaches(text, broken);
        }

        foreach (var note in report.Notes)
        {
            text.AppendLine().Append("Note: ").AppendLine(note);
        }

        // An id rule of a child entity's table is reported under the child, which no root count names.
        var others = report.Breaches.Where(b => !report.Read.Any(r => string.Equals(r.Aggregate, b.Aggregate, StringComparison.Ordinal))).ToList();
        foreach (var group in others.GroupBy(static b => b.Aggregate, StringComparer.Ordinal))
        {
            text.AppendLine().Append(group.Key).AppendLine(":");
            Breaches(text, [.. group]);
        }

        return text.ToString();
    }

    /// <summary>The report as JSON: <c>{ "clean", "read": [{ aggregate, rows }], "breaches": [{ aggregate, field, rule, count, ids }] }</c>.</summary>
    internal static string Json(RuleAuditReport report)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteBoolean("clean", report.Clean);
            json.WriteStartArray("read");
            foreach (var read in report.Read)
            {
                json.WriteStartObject();
                json.WriteString("aggregate", read.Aggregate);
                json.WriteNumber("rows", read.Rows);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("breaches");
            foreach (var breach in report.Breaches)
            {
                json.WriteStartObject();
                json.WriteString("aggregate", breach.Aggregate);
                json.WriteString("field", breach.Field);
                json.WriteString("rule", breach.Rule);
                json.WriteNumber("count", breach.Ids.Count);
                json.WriteStartArray("ids");
                foreach (var id in breach.Ids)
                {
                    json.WriteStringValue(id);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("notes");
            foreach (var note in report.Notes)
            {
                json.WriteStringValue(note);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Breaches(StringBuilder text, List<RuleBreach> breaches)
    {
        foreach (var breach in breaches)
        {
            var field = breach.Field.Length == 0 ? "(the aggregate)" : breach.Field;
            text.Append(CultureInfo.InvariantCulture, $"  {field}: {breach.Rule} — {breach.Ids.Count}").AppendLine();
            text.Append("    ").AppendJoin(", ", breach.Ids.Take(Shown));
            if (breach.Ids.Count > Shown)
            {
                text.Append(CultureInfo.InvariantCulture, $", … and {breach.Ids.Count - Shown} more (--json lists every one)");
            }

            text.AppendLine();
        }
    }
}
