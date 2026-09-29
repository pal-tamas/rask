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

    // Every combination of the arguments' types that crosses the wire, each with how it goes into the call: a value
    // as itself, a callback (an IDL callback whose arguments are values) as a C# handler handed over by JsChain.Callback.
    // A method with one callback takes it sync or async; with more, each is an Action.
    internal static IEnumerable<List<(string Type, string Name, string Arg)>> Expand(List<JsonNode> args, DomValueTypes types, JsonNode? callbacks)
    {
        IEnumerable<List<(string Type, string Name, string Arg)>> combos = new[] { new List<(string Type, string Name, string Arg)>() };
        foreach (var a in args)
        {
            var name = DomRefEmitter.Identifier(a["name"]!.AsString()!);
            var alternatives = new List<(string Type, string Arg)>();
            foreach (var t in DomValueTypes.Alternatives(a["type"]!.AsString()!))
            {
                if (types.CSharp(t, returned: false) is { } value)
                {
                    alternatives.Add((value, name));
                }
                else
                {
                    alternatives.AddRange(Handlers(t, types, callbacks).Select(h => (h, $"global::Rask.Web.JsChain.Callback({name})")));
                }
            }

            combos = combos.SelectMany(c => alternatives.Select(t => new List<(string Type, string Name, string Arg)>(c) { (t.Type, name, t.Arg) })).ToList();
        }

        return combos.Where(c => !c.Any(p => p.Type.StartsWith("global::System.Func<", StringComparison.Ordinal))
                                 || c.Count(p => p.Arg.StartsWith("global::Rask.Web.JsChain.Callback", StringComparison.Ordinal)) == 1);
    }

    // The C# handler types an IDL callback can be written as: Action<…> and Func<…, Task>, for one that returns nothing
    // and whose arguments are values.
    private static IEnumerable<string> Handlers(string idl, DomValueTypes types, JsonNode? callbacks)
    {
        var nullable = idl.EndsWith("?", StringComparison.Ordinal) ? "?" : "";
        if (callbacks?[idl.TrimEnd('?')] is not { } callback || !string.Equals(callback["returns"]?.AsString(), "undefined", StringComparison.Ordinal))
        {
            yield break;
        }

        var args = callback["args"]?.Items ?? new List<JsonNode>();
        var mapped = args.Select(x => types.CSharp(x["type"]!.AsString()!, returned: false)).ToList();
        if (args.Count > 3 || mapped.Any(x => x is null) || args.Any(x => x["variadic"]?.AsBoolean() == true))
        {
            yield break;
        }

        yield return mapped.Count == 0 ? "global::System.Action" + nullable : $"global::System.Action<{string.Join(", ", mapped)}>{nullable}";
        if (mapped.Count <= 1)
        {
            yield return mapped.Count == 0
                ? "global::System.Func<global::System.Threading.Tasks.Task>" + nullable
                : $"global::System.Func<{mapped[0]}, global::System.Threading.Tasks.Task>{nullable}";
        }
    }

    // An interface's events, each as On{Event}(handler): sync or async, with the event or without it. The subscription
    // they return removes the listener when disposed of; so does the handler's component unmounting.
    public static List<WebMember> EventsOf(JsonNode data, WebPayloads payloads, HashSet<string> taken)
    {
        var result = new List<WebMember>();
        foreach (var e in data["events"]?.Items ?? new List<JsonNode>())
        {
            var type = e["type"]!.AsString()!;
            var name = DomEventEmitter.HandlerName(type);
            if (!taken.Add(name))
            {
                continue;
            }

            var (payload, fields) = payloads.Of(e["interface"]?.AsString() ?? "Event");
            var head = $"Chain.Listen(\"{type}\", {fields}, handler, static p => new {payload}(p), ";
            var summary = $"MDN's <c>{type}</c> event: the handler runs, in its component's order, each time it fires. Dispose of the subscription to stop.";
            const string returns = "ValueTask<global::System.IAsyncDisposable>";
            result.Add(new WebMember(e, summary, returns, name, $"global::System.Action<{payload}> handler", "handler",
                head + "e => { handler(e); return global::System.Threading.Tasks.Task.CompletedTask; })"));
            result.Add(new WebMember(e, summary, returns, name, $"global::System.Func<{payload}, global::System.Threading.Tasks.Task> handler", "handler",
                head + "handler)"));
            result.Add(new WebMember(e, summary, returns, name, "global::System.Action handler", "handler",
                head + "_ => { handler(); return global::System.Threading.Tasks.Task.CompletedTask; })"));
            result.Add(new WebMember(e, summary, returns, name, "global::System.Func<global::System.Threading.Tasks.Task> handler", "handler",
                head + "_ => handler())"));
        }

        return result;
    }

    private WebMember AsStatic(string root) =>
        new(_data, _summary, _returns, Name, _parameters, _arguments, _body.Replace("Chain.", root + "."), "static ", ParameterTypes);

    public static List<WebMember> Of(
        string iface, JsonNode data, DomValueTypes types, HashSet<string> proxies, HashSet<string> taken, HashSet<string> denied, JsonNode? callbacks = null)
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
                ? Operation(m, idl, types, proxies, callbacks)
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

    private static List<WebMember> Operation(JsonNode m, string idl, DomValueTypes types, HashSet<string> proxies, JsonNode? callbacks)
    {
        var result = new List<WebMember>();
        var returns = m["returns"]!.AsString()!;
        var method = DomRefEmitter.Pascal(idl);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var lists = new List<List<JsonNode>> { m["args"]?.Items ?? new List<JsonNode>() };
        lists.AddRange((m["overloads"]?.Items ?? new List<JsonNode>()).Select(o => o.Items));
        foreach (var args in lists.Where(a => !a.Any(x => x["variadic"]?.AsBoolean() == true)))
        {
            var distinct = DomRefEmitter.Prefixes(args).SelectMany(prefix => Expand(prefix, types, callbacks))
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
        JsonNode m, string idl, string method, string returns, List<(string Type, string Name, string Arg)> parameters, DomValueTypes types, HashSet<string> proxies)
    {
        var declared = string.Join(", ", parameters.Select(p => p.Type + " " + p.Name));
        var names = string.Join(", ", parameters.Select(p => p.Name));
        var values = string.Join(", ", parameters.Select(p => p.Arg));
        // Always an explicit array: a lone string[] argument would otherwise BE the params array (array covariance).
        var args = parameters.Count == 0 ? "" : ", new object?[] { " + values + " }";
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
