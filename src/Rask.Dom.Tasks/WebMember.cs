using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// One generated member of a Rask.Web proxy, written as an instance member over the proxy's chain and, for a global, as
// a static that forwards to the global's instance.
//   an attribute holding a live object → a proxy one step further along the chain (no round trip)
//   an attribute holding a value       → an awaitable read, and Set{Name} where it is writable
//   an operation returning a value     → a method that runs the chain (Promise<T> awaited)
//   an operation returning an object   → a proxy over the call; kept (a handle) when the object comes by Promise
//   a static member                    → the same, on the class, from window.{Interface}
//   a constructor                      → static Create(…), the new object kept
internal sealed class WebMember
{
    private const string TypesNs = "global::Rask.Web.Types.";

    private readonly JsonNode _data;
    private readonly string _summary;
    private readonly string _returns;
    private readonly string? _parameters;
    private readonly string _arguments;
    private readonly string _body;

    // "" for an instance member, "static " or "static new " (hiding a base's Create of the same parameters).
    private readonly string _modifiers;

    private WebMember(
        JsonNode data, string summary, string returns, string name, string? parameters, string arguments, string body, string modifiers = "",
        string parameterTypes = "")
    {
        _data = data;
        _summary = summary;
        _returns = returns;
        Name = name;
        _parameters = parameters;
        _arguments = arguments;
        _body = body;
        _modifiers = modifiers;
        ParameterTypes = parameterTypes;
    }

    public string Name { get; }

    // The parameter types alone: how a derived Create is told apart from, or hides, a base's.
    public string ParameterTypes { get; }

    // Name and parameter types: what makes two members the same overload.
    public string Signature => Name + "(" + (_parameters ?? "") + ")";

    // The static members: the class's own, reached from window.{Interface} (URL.CanParse, Notification.Permission).
    public static List<WebMember> StaticsOf(string iface, JsonNode data, DomValueTypes types, HashSet<string> proxies, HashSet<string> taken)
    {
        var root = $"global::Rask.Web.JsChain.Window.Get(\"{iface}\")";
        var statics = new JsonNode(JsonKind.Object, 0, 0);
        statics.Members.Add(new KeyValuePair<string, JsonNode>("members", data["statics"] ?? new JsonNode(JsonKind.Array, 0, 0)));
        return Of(iface, statics, types, proxies, taken, new HashSet<string>(StringComparer.Ordinal))
            .Select(m => m.AsStatic(root)).ToList();
    }

    // `new X(…)` as X.Create(…): the new object, kept in the browser. `hidden` holds the parameter types of a base's
    // Create this one hides (EventTarget has a constructor too).
    public static List<WebMember> ConstructorsOf(string iface, JsonNode data, DomValueTypes types, HashSet<string> hidden)
    {
        var result = new List<WebMember>();
        var proxy = TypesNs + iface;
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ctor in data["constructors"]?.Items ?? new List<JsonNode>())
        {
            var args = ctor["args"]?.Items ?? new List<JsonNode>();
            if (args.Any(x => x["variadic"]?.AsBoolean() == true))
            {
                continue;
            }

            var distinct = DomRefEmitter.Prefixes(args).SelectMany(prefix => DomRefEmitter.Expand(prefix, types))
                .Where(p => signatures.Add(string.Join(",", p.Select(x => x.Type))));
            foreach (var parameters in distinct)
            {
                var typesOnly = string.Join(",", parameters.Select(p => p.Type));
                var names = string.Join(", ", parameters.Select(p => p.Name));
                var array = parameters.Count == 0 ? "" : ", new object?[] { " + names + " }";
                result.Add(new WebMember(ctor, $"MDN's <c>new {iface}()</c>: the new object, kept in the browser until you dispose of it.",
                    $"ValueTask<{proxy}>", "Create", string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)), names,
                    $"global::Rask.Web.JsChain.Window.New(\"{iface}\"{array}).Keep(static c => new {proxy}(c))",
                    hidden.Contains(typesOnly) ? "static new " : "static ", typesOnly));
            }
        }

        return result;
    }

    private WebMember AsStatic(string root) =>
        new(_data, _summary, _returns, Name, _parameters, _arguments, _body.Replace("Chain.", root + "."), "static ", ParameterTypes);

    public static List<WebMember> Of(
        string iface, JsonNode data, DomValueTypes types, HashSet<string> proxies, HashSet<string> taken, HashSet<string> denied)
    {
        var result = new List<WebMember>();
        foreach (var m in data["members"]?.Items ?? new List<JsonNode>())
        {
            var idl = m["name"]!.AsString()!;
            // CSS's dashed twins (font-display beside fontDisplay) are not identifiers, and the camel-cased one is there.
            if (denied.Contains(iface + "." + idl) || !idl.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                continue;
            }

            var added = string.Equals(m["kind"]?.AsString(), "operation", StringComparison.Ordinal)
                ? Operation(m, idl, types, proxies)
                : Attribute(m, idl, types, proxies, denied.Contains(iface + "." + idl + "="));
            result.AddRange(added.Where(x => !taken.Contains(x.Name)));
            taken.UnionWith(added.Select(x => x.Name));
        }

        return result;
    }

    private static List<WebMember> Attribute(JsonNode m, string idl, DomValueTypes types, HashSet<string> proxies, bool readOnly)
    {
        var result = new List<WebMember>();
        var idlType = m["type"]!.AsString()!;
        var prop = DomRefEmitter.Pascal(idl);
        if (proxies.Contains(idlType.TrimEnd('?')))
        {
            result.Add(new WebMember(m, $"MDN's <c>{idl}</c>: the object, one step further along the chain.", TypesNs + idlType.TrimEnd('?'), prop, null, "",
                $"new(Chain.Get(\"{idl}\"))"));
            return result;
        }

        if (types.CSharp(idlType, returned: true) is not { } value)
        {
            return result;
        }

        // A Promise<undefined> attribute (a view transition's `finished`) is something to wait for, not a value.
        result.Add(string.Equals(value, "void", StringComparison.Ordinal)
            ? new WebMember(m, $"MDN's <c>{idl}</c>: completes when it settles in the browser.", "ValueTask", prop, null, "", $"Chain.Settle(\"{idl}\")")
            : new WebMember(m, $"MDN's <c>{idl}</c>, read from the browser.", $"ValueTask<{value}>", prop, null, "", $"Chain.Read<{value}>(\"{idl}\")"));
        if (!readOnly && m["readonly"]?.AsBoolean() != true && types.CSharp(idlType, returned: false) is { } arg)
        {
            result.Add(new WebMember(m, $"Sets MDN's <c>{idl}</c> in the browser.", "ValueTask", "Set" + prop, arg + " value", "value", $"Chain.Write(\"{idl}\", value)"));
        }

        return result;
    }

    private static List<WebMember> Operation(JsonNode m, string idl, DomValueTypes types, HashSet<string> proxies)
    {
        var result = new List<WebMember>();
        var returns = m["returns"]!.AsString()!;
        var method = DomRefEmitter.Pascal(idl);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var lists = new List<List<JsonNode>> { m["args"]?.Items ?? new List<JsonNode>() };
        lists.AddRange((m["overloads"]?.Items ?? new List<JsonNode>()).Select(o => o.Items));
        foreach (var args in lists.Where(a => !a.Any(x => x["variadic"]?.AsBoolean() == true)))
        {
            var distinct = DomRefEmitter.Prefixes(args).SelectMany(prefix => DomRefEmitter.Expand(prefix, types))
                .Where(p => signatures.Add(string.Join(",", p.Select(x => x.Type))));
            foreach (var parameters in distinct)
            {
                if (Call(m, idl, method, returns, parameters, types, proxies) is { } member)
                {
                    result.Add(member);
                }
            }
        }

        return result;
    }

    private static WebMember? Call(
        JsonNode m, string idl, string method, string returns, List<(string Type, string Name)> parameters, DomValueTypes types, HashSet<string> proxies)
    {
        var declared = string.Join(", ", parameters.Select(p => p.Type + " " + p.Name));
        var names = string.Join(", ", parameters.Select(p => p.Name));
        // Always an explicit array: a lone string[] argument would otherwise BE the params array (array covariance).
        var args = parameters.Count == 0 ? "" : ", new object?[] { " + names + " }";
        var promised = returns.StartsWith("Promise<", StringComparison.Ordinal) ? returns.Substring(8, returns.Length - 9) : null;
        var summary = $"MDN's <c>{idl}()</c>, called in the browser.";
        if (promised is not null && proxies.Contains(promised.TrimEnd('?')))
        {
            var proxy = TypesNs + promised.TrimEnd('?');
            return new WebMember(m, summary + " The object it resolves to is kept: dispose of it when done.", $"ValueTask<{proxy}>", method, declared, names,
                $"Chain.Invoke(\"{idl}\"{args}).Keep(static c => new {proxy}(c))");
        }

        if (promised is null && proxies.Contains(returns.TrimEnd('?')))
        {
            var proxy = TypesNs + returns.TrimEnd('?');
            return new WebMember(m, summary + " Nothing runs until a member of the result is awaited.", proxy, method, declared, names,
                $"new(Chain.Invoke(\"{idl}\"{args}))");
        }

        if (types.CSharp(returns, returned: true) is not { } value)
        {
            return null;
        }

        var typesOnly = string.Join(",", parameters.Select(p => p.Type));
        return string.Equals(value, "void", StringComparison.Ordinal)
            ? new WebMember(m, summary, "ValueTask", method, declared, names, $"Chain.Call(\"{idl}\"{args})", parameterTypes: typesOnly)
            : new WebMember(m, summary, $"ValueTask<{value}>", method, declared, names, $"Chain.Call<{value}>(\"{idl}\"{args})", parameterTypes: typesOnly);
    }

    public void WriteInstance(StringBuilder sb)
    {
        DomEmitter.Doc(sb, "    ", _summary, _data);
        sb.Append("    public ").Append(_modifiers).Append(_returns).Append(' ').Append(Name);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_parameters).Append(')');
        }

        sb.Append(" => ").Append(_body).AppendLine(";");
    }

    // A static or constructor on its class in Rask.Web: no base to hide there, so plainly static.
    public void WriteFacade(StringBuilder sb)
    {
        DomEmitter.Doc(sb, "    ", _summary, _data);
        sb.Append("    public static ").Append(_returns).Append(' ').Append(Name);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_parameters).Append(')');
        }

        sb.Append(" => ").Append(_body).AppendLine(";");
    }

    public void WriteStatic(StringBuilder sb)
    {
        DomEmitter.Doc(sb, "    ", _summary, _data);
        sb.Append("    public static ").Append(_returns).Append(' ').Append(Name);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_parameters).Append(')');
        }

        sb.Append(" => Instance.").Append(Name);
        if (_parameters is not null)
        {
            sb.Append('(').Append(_arguments).Append(')');
        }

        sb.AppendLine(";");
    }
}
