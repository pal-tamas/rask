using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// The C# type an event handed to a Rask.Web handler arrives as. Core's event classes where Core has one (Event,
// MouseEvent: the element events'), so an Event is one type everywhere; the rest (MediaQueryListEvent, StorageEvent) are
// written here, in Rask.Web.Types, deriving from the nearest one Core has. Each reads the fields of its interface whose
// types are values, and only those cross: the listener sends exactly what the type reads.
internal sealed class WebPayloads(JsonNode root, DomValueTypes types)
{
    private const string TypesNs = "global::Rask.Web.Types.";
    private const string CoreNs = "global::Rask.Core.";

    private readonly JsonNode _interfaces = root["interfaces"]!;
    private readonly HashSet<string> _core = CoreClasses(root);
    private readonly HashSet<string> _coreSealed = CoreSealed(root);
    private readonly SortedDictionary<string, List<(string Property, string Type, string Json, JsonNode Data)>> _declared = new(StringComparer.Ordinal);

    // The payload type and the JSON array of field names its listener sends.
    public (string Type, string Fields) Of(string iface)
    {
        var name = _interfaces[iface] is null ? "Event" : iface;
        Declare(name);
        var fields = Fields(name).Select(f => "\\\"" + f + "\\\"");
        return (Qualified(name), "\"[" + string.Join(",", fields) + "]\"");
    }

    private string Qualified(string name) => _core.Contains(name) ? CoreNs + name : TypesNs + name;

    // Which of Core's event classes it seals: the ones none of the others derives from.
    private static HashSet<string> CoreSealed(JsonNode root)
    {
        var interfaces = root["interfaces"]!;
        var core = CoreClasses(root);
        var parents = new HashSet<string>(core.Select(n => interfaces[n]!["parent"]?.AsString()).OfType<string>(), StringComparer.Ordinal);
        return new HashSet<string>(core.Where(n => !parents.Contains(n)), StringComparer.Ordinal);
    }

    // Core's event classes: every element event's interface and its ancestors, as DomEventEmitter writes them.
    private static HashSet<string> CoreClasses(JsonNode root)
    {
        var interfaces = root["interfaces"]!;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root["events"]?.Items ?? new List<JsonNode>())
        {
            for (var n = e["interface"]?.AsString(); n is not null && interfaces[n] is not null; n = interfaces[n]!["parent"]?.AsString())
            {
                result.Add(n);
            }
        }

        return result;
    }

    private void Declare(string name)
    {
        if (_core.Contains(name) || _declared.ContainsKey(name))
        {
            return;
        }

        var parent = _interfaces[name]!["parent"]?.AsString();
        if (parent is not null)
        {
            if (_coreSealed.Contains(parent))
            {
                throw new DomEmitException($"{name} derives from {parent}, which Core seals. Unseal it in DomEventEmitter, or give {name} another base.");
            }

            Declare(parent);
        }

        var inherited = new HashSet<string>(parent is null ? Enumerable.Empty<string>() : Fields(parent), StringComparer.Ordinal);
        _declared[name] = Own(name).Where(f => !inherited.Contains(f.Json)).ToList();
    }

    // An interface's attributes whose types are values.
    private IEnumerable<(string Property, string Type, string Json, JsonNode Data)> Own(string name)
    {
        foreach (var m in _interfaces[name]!["members"]?.Items ?? new List<JsonNode>())
        {
            if (string.Equals(m["kind"]?.AsString(), "attribute", StringComparison.Ordinal)
                && types.CSharp(m["type"]!.AsString()!, returned: false) is { } type)
            {
                yield return (DomRefEmitter.Pascal(m["name"]!.AsString()!), type, m["name"]!.AsString()!, m);
            }
        }
    }

    // Every field the payload reads: its own and its ancestors'.
    private List<string> Fields(string name)
    {
        var result = new List<string>();
        for (var n = name; n is not null && _interfaces[n] is not null; n = _interfaces[n]!["parent"]?.AsString())
        {
            result.AddRange(Own(n).Select(f => f.Json).Where(f => !result.Contains(f)));
        }

        return result;
    }

    public string Declarations()
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("namespace Rask.Web.Types;");
        var parents = new HashSet<string>(_declared.Keys.Select(n => _interfaces[n]!["parent"]?.AsString()).OfType<string>(), StringComparer.Ordinal);
        foreach (var payload in _declared)
        {
            Class(sb, payload.Key, parents.Contains(payload.Key), payload.Value);
        }

        return sb.ToString();
    }

    private void Class(StringBuilder sb, string name, bool hasChildren, List<(string Property, string Type, string Json, JsonNode Data)> fields)
    {
        var parent = _interfaces[name]!["parent"]?.AsString();
        sb.AppendLine();
        DomEmitter.Doc(sb, "", $"MDN's <c>{name}</c>, as a Rask.Web handler receives it: the event's fields, read when it fired.", _interfaces[name]!);
        sb.Append("public ").Append(hasChildren ? "" : "sealed ").Append("class ").Append(name);
        sb.AppendLine(parent is null ? "" : " : " + Qualified(parent));
        sb.AppendLine("{");
        sb.Append("    /// <summary>An empty ").Append(name).AppendLine(", for a test to fill.</summary>");
        sb.Append("    public ").Append(name).AppendLine("()");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    internal ").Append(name).Append("(global::System.Text.Json.JsonElement p)").AppendLine(parent is null ? "" : " : base(p)");
        sb.AppendLine("    {");
        foreach (var (property, type, json, _) in fields)
        {
            sb.Append("        ").Append(property).Append(" = global::Rask.Web.JsChain.Field<").Append(type).Append(">(p, \"").Append(json).AppendLine("\");");
        }

        sb.AppendLine("    }");
        foreach (var (property, type, json, data) in fields)
        {
            sb.AppendLine();
            DomEmitter.Doc(sb, "    ", $"The event's <c>{json}</c>.", data);
            // A non-nullable reference (a string, an array, a record) starts as default! for the empty constructor.
            var valueOrNullable = type.EndsWith("?", StringComparison.Ordinal) || type is "bool" or "int" or "long" or "double" || types.IsEnum(type);
            sb.Append("    public ").Append(type).Append(' ').Append(property).Append(" { get; init; }").AppendLine(valueOrNullable ? "" : " = default!;");
        }

        sb.AppendLine("}");
    }
}
