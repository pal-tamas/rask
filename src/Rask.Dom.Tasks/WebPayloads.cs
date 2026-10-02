using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// The C# type an event handed to a Rask.Web handler arrives as. Core's event classes where Core has one (Event,
// MouseEvent: the element events'), so an Event is one type everywhere; the rest (MediaQueryListEvent, StorageEvent) are
// written here, in Rask.Web.Types, deriving from the nearest one Core has. Each reads the fields of its interface whose
// types are values, and only those cross: the listener sends exactly what the type reads. A field holding a live object
// (`proxies`: a USB connection's device) crosses as one kept for the handler, `*device` in that list.
internal sealed class WebPayloads(JsonNode root, DomValueTypes types, HashSet<string> proxies)
{
    private const string TypesNs = "global::Rask.Web.Types.";
    private const string CoreNs = "global::Rask.Core.";

    private readonly JsonNode _interfaces = root["interfaces"]!;
    private readonly HashSet<string> _core = CoreClasses(root);
    private readonly HashSet<string> _coreSealed = CoreSealed(root);
    private readonly Dictionary<string, List<string>> _firedOn = FiredOn(root);
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

        var inherited = new HashSet<string>(parent is null ? Enumerable.Empty<string>() : Fields(parent).Select(f => f.TrimStart('*')), StringComparer.Ordinal);
        _declared[name] = Own(name).Where(f => !inherited.Contains(f.Json)).ToList();
        _methods[name] = Methods(name);
    }

    // An event's own methods, called on the event itself later — a BeforeInstallPromptEvent kept from the handler and
    // prompted in a click (CalledLater). An event that has any is kept whole for its handler, `*` (the event itself) in
    // its listener's list, and its methods run over that handle until the handler's component unmounts.
    private readonly Dictionary<string, List<WebMember>> _methods = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, List<WebMember>> Methods() => _methods;

    // The events whose methods are made to be called after the event: most must run while it dispatches (a navigation's
    // intercept(), preventDefault()), which a handler on the far side of a round trip has already missed.
    private static readonly HashSet<string> CalledLater = new(StringComparer.Ordinal) { "BeforeInstallPromptEvent" };

    private List<WebMember> Methods(string name)
    {
        if (!CalledLater.Contains(name))
        {
            return new List<WebMember>();
        }

        var operations = (_interfaces[name]!["members"]?.Items ?? new List<JsonNode>())
            .Where(m => string.Equals(m["kind"]?.AsString(), "operation", StringComparison.Ordinal));
        var taken = new HashSet<string>(_declared[name].Select(f => f.Property), StringComparer.Ordinal) { "Chain", name };
        return WebMember.Of(name, WebMember.Holding(operations), types, proxies, taken, new HashSet<string>(StringComparer.Ordinal), root["callbacks"]);
    }

    // Whether the event, or one it derives from, is kept whole for its methods.
    private bool Keeps(string name)
    {
        for (var n = name; n is not null && _methods.TryGetValue(n, out var methods); n = _interfaces[n]!["parent"]?.AsString())
        {
            if (methods.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    // An interface's attributes whose types are values, its `any` ones (a message's data) as Any, and its live ones.
    private IEnumerable<(string Property, string Type, string Json, JsonNode Data)> Own(string name)
    {
        foreach (var m in _interfaces[name]!["members"]?.Items ?? new List<JsonNode>())
        {
            if (string.Equals(m["kind"]?.AsString(), "attribute", StringComparison.Ordinal) && m["type"]!.AsString() is { } idl
                && (string.Equals(idl, "any", StringComparison.Ordinal) ? Any : types.CSharp(idl, returned: false) ?? Live(name, idl)) is { } type)
            {
                yield return (DomRefEmitter.Pascal(m["name"]!.AsString()!), type, m["name"]!.AsString()!, m);
            }
        }
    }

    private const string Any = "any";

    // A field holding a live object, kept for the handler — unless the event only ever fires on that object (a HID
    // device's input report, many a second), which the handler already holds: keeping it again per event would pile up
    // handles. One only WebAssembly runs (a GPU error) is Rask.Wasm's, which this assembly cannot name.
    private string? Live(string iface, string idl)
    {
        // Core's own event classes (Event: its target) read no live field, so their listeners keep none.
        if (_core.Contains(iface))
        {
            return null;
        }

        var live = idl.TrimEnd('?');
        var targets = _firedOn.TryGetValue(iface, out var t) ? t : new List<string>();
        var held = targets.Count > 0 && targets.All(target => DomValueTypes.Derives(_interfaces, target, live));
        return proxies.Contains(live) && !WebHost.IsWasmInterface(live) && !held ? TypesNs + idl : null;
    }

    private bool IsLive(string type) => type.StartsWith(TypesNs, StringComparison.Ordinal) && proxies.Contains(type.Substring(TypesNs.Length).TrimEnd('?'));

    // Which interfaces fire events of each payload interface: USBConnectionEvent fires on USB.
    private static Dictionary<string, List<string>> FiredOn(JsonNode root)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var target in root["interfaces"]!.Members)
        {
            foreach (var e in target.Value["events"]?.Items ?? new List<JsonNode>())
            {
                var payload = e["interface"]?.AsString() ?? "Event";
                if (!result.TryGetValue(payload, out var list))
                {
                    result[payload] = list = new List<string>();
                }

                list.Add(target.Key);
            }
        }

        return result;
    }

    // Every field the payload reads, its own and its ancestors', as its listener asks for them: a live one as `*name`,
    // and the event itself as `*` where it is kept for its methods.
    private List<string> Fields(string name)
    {
        var result = new List<string>();
        if (Keeps(name))
        {
            result.Add("*");
        }

        for (var n = name; n is not null && _interfaces[n] is not null; n = _interfaces[n]!["parent"]?.AsString())
        {
            result.AddRange(Own(n).Select(f => (IsLive(f.Type) ? "*" : "") + f.Json).Where(f => !result.Contains(f)));
        }

        return result;
    }

    public string Declarations()
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
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
        var keepsItself = Keeps(name) && (parent is null || !Keeps(parent));
        if (keepsItself)
        {
            sb.AppendLine("        Chain = global::Rask.Web.JsChain.KeptField(p, \"\", static c => c);");
        }

        foreach (var (property, type, json, _) in fields)
        {
            sb.Append("        ").Append(string.Equals(type, Any, StringComparison.Ordinal) ? "_" + json : property).Append(" = global::Rask.Web.JsChain.");
            sb.Append(Read(type)).Append("(p, \"").Append(json).Append('"');
            sb.AppendLine(IsLive(type) ? $", static c => new {type.TrimEnd('?')}(c));" : ");");
        }

        sb.AppendLine("    }");
        if (keepsItself)
        {
            // Only an event the browser fired has one: a test's empty one has no event to call its methods on.
            sb.AppendLine();
            sb.AppendLine("    internal global::Rask.Web.JsChain Chain { get; } = null!;");
        }

        foreach (var method in _methods[name].Where(m => !m.Wasm))
        {
            sb.AppendLine();
            method.WriteInstance(sb);
        }

        foreach (var (property, type, json, data) in fields)
        {
            sb.AppendLine();
            if (string.Equals(type, Any, StringComparison.Ordinal))
            {
                AnyField(sb, property, json, data);
                continue;
            }

            DomEmitter.Doc(sb, "    ", IsLive(type)
                ? $"The event's <c>{json}</c>, kept in the browser until you dispose of it or the handler's component unmounts."
                : $"The event's <c>{json}</c>.", data);
            // A non-nullable reference (a string, an array, a record) starts as default! for the empty constructor.
            var valueOrNullable = type.EndsWith("?", StringComparison.Ordinal) || type is "bool" or "int" or "long" or "double" || types.IsEnum(type);
            sb.Append("    public ").Append(type).Append(' ').Append(property).Append(" { get; init; }").AppendLine(valueOrNullable ? "" : " = default!;");
        }

        sb.AppendLine("}");
    }

    // How the constructor reads a field: as the handler's own type later, as a kept object, or as a value.
    private string Read(string type) => type switch
    {
        Any => "AnyFieldOf",
        _ when IsLive(type) => "KeptField",
        _ => "Field<" + type + ">",
    };

    // An `any` field is the sender's own type, which only the handler knows: kept as JSON, read when it asks, e.Data<T>().
    private static void AnyField(StringBuilder sb, string property, string json, JsonNode data)
    {
        sb.Append("    private readonly global::Rask.Web.JsChain.AnyField _").Append(json).AppendLine(";");
        sb.AppendLine();
        DomEmitter.Doc(sb, "    ", $"The event's <c>{json}</c>, read as your own type.", data);
        sb.Append("    public T ").Append(property).Append('<').Append(WebMember.JsonMembers).Append("T>() => _").Append(json).AppendLine(".As<T>();");
    }
}
