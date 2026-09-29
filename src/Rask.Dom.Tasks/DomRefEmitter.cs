using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// The DOM members a typed ElementRef<T> carries, generated from MDN's IDL onto IElementRef<Interface> as C# 14
// extension members: an operation is a method (`ShowModal()`), an attribute an awaitable read (`await _dialog.Open`)
// and, where Rask's render does not own it, a write (`SetCurrentTime(12)`). Only members whose types cross the wire
// as JSON are generated; the IDL enums, dictionaries and self-serializing value objects (DOMRect) they use become
// C# enums and records (DomValueTypes), with a source-generated JSON context for trimmed WASM.
internal static class DomRefEmitter
{
    // Rask owns the DOM tree, the attributes it renders and the content it diffs: a member that rewrites any of them
    // would be undone by the next render, or would undo the render. Node and EventTarget are left out whole.
    private static readonly HashSet<string> RenderOwned = new(StringComparer.Ordinal)
    {
        "innerHTML", "outerHTML", "innerText", "outerText", "textContent", "setAttribute", "setAttributeNS", "removeAttribute",
        "removeAttributeNS", "toggleAttribute", "setAttributeNode", "setAttributeNodeNS", "removeAttributeNode", "attachShadow",
        "attachInternals", "insertAdjacentElement", "insertAdjacentText", "insertAdjacentHTML", "setHTML", "setHTMLUnsafe",
        "append", "prepend", "replaceChildren", "before", "after", "remove", "replaceWith", "moveBefore", "add",
        "deleteRow", "deleteCell", "deleteCaption", "deleteTHead", "deleteTFoot",
    };

    // Names an ElementRef answers itself: an extension of the same name would never be reached.
    private static readonly HashSet<string> RefMembers = new(StringComparer.Ordinal)
    {
        "Id", "ToString", "Equals", "GetHashCode", "GetType", "Target", "Element", "Runtime",
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "object", "string", "event", "base", "class", "default", "in", "is", "as", "operator", "params", "ref", "out", "checked",
        "fixed", "lock", "namespace", "new", "null", "this", "throw", "true", "false", "type", "value", "volatile",
    };

    // What an untyped ElementRef carries, ahead of every element interface.
    private static readonly string[] Untyped = { "Element" };

    public static void Emit(JsonNode root, IReadOnlyList<string> dom, IReadOnlyDictionary<string, HashSet<string>> rendered, List<KeyValuePair<string, string>> files)
    {
        var types = new DomValueTypes(root);
        var interfaces = root["interfaces"]!;
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Rask.Core.Components;");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The DOM members each MDN interface gives an <see cref=\"IElementRef{T}\" /> of it, generated from MDN.</summary>");
        sb.AppendLine("public static partial class ElementRefMembers");
        sb.AppendLine("{");

        // Bases before derived, so a member a base already carries is not declared again (it reaches the derived
        // ref through IElementRef's covariance).
        var names = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var name in Untyped.Concat(dom))
        {
            var parent = interfaces[name]?["parent"]?.AsString();
            var taken = parent is not null && names.TryGetValue(parent, out var inherited)
                ? new HashSet<string>(inherited, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            names[name] = taken;
            var owned = rendered.TryGetValue(name, out var r) ? r : new HashSet<string>(StringComparer.Ordinal);
            var body = Members(interfaces[name]!, types, owned, taken);
            if (body.Length > 0)
            {
                var receiver = string.Equals(name, "Element", StringComparison.Ordinal) ? "global::Rask.Core.Element" : name;
                sb.Append("    extension(IElementRef<").Append(receiver).AppendLine("> element)");
                sb.AppendLine("    {");
                sb.Append(body);
                sb.AppendLine("    }");
                sb.AppendLine();
            }
        }

        sb.AppendLine("}");
        files.Add(new KeyValuePair<string, string>("ElementRefMembers.g.cs", sb.ToString()));
        files.Add(new KeyValuePair<string, string>("DomValues.g.cs", types.Declarations()));
    }

    private static string Members(JsonNode iface, DomValueTypes types, HashSet<string> rendered, HashSet<string> taken)
    {
        var sb = new StringBuilder();
        foreach (var m in iface["members"]?.Items ?? new List<JsonNode>())
        {
            var idl = m["name"]!.AsString()!;
            if (RenderOwned.Contains(idl))
            {
                continue;
            }

            if (string.Equals(m["kind"]?.AsString(), "operation", StringComparison.Ordinal))
            {
                Operation(sb, m, idl, types, taken);
            }
            else
            {
                Attribute(sb, m, idl, types, rendered, taken);
            }
        }

        return sb.ToString();
    }

    private static void Attribute(StringBuilder sb, JsonNode m, string idl, DomValueTypes types, HashSet<string> rendered, HashSet<string> taken)
    {
        var idlType = m["type"]!.AsString()!;
        var type = types.CSharp(idlType, returned: true);
        if (type is null)
        {
            return;
        }

        var prop = Pascal(idl);
        if (!RefMembers.Contains(prop) && taken.Add(prop))
        {
            DomEmitter.Doc(sb, "        ", $"MDN's <c>{idl}</c>, read from the live element.", m);
            sb.Append("        public ValueTask<").Append(type).Append("> ").Append(prop)
                .Append(" => ElementRefCall.Get<").Append(type).Append(">(element.Target, \"").Append(idl).AppendLine("\");");
            sb.AppendLine();
        }

        // A write only where Rask's render does not own the value: `open` is the dialog's attribute and the render's
        // to set, `currentTime` is the video's own state.
        var setter = "Set" + prop;
        var ownedByRender = m["readonly"]?.AsBoolean() == true || m["reflect"] is not null || rendered.Contains(prop);
        var argType = ownedByRender ? null : types.CSharp(idlType, returned: false);
        if (argType is null || !taken.Add(setter))
        {
            return;
        }

        DomEmitter.Doc(sb, "        ", $"Sets MDN's <c>{idl}</c> on the live element.", m);
        sb.Append("        public ValueTask ").Append(setter).Append('(').Append(argType).Append(" value) => ElementRefCall.Set(element.Target, \"")
            .Append(idl).AppendLine("\", value);");
        sb.AppendLine();
    }

    private static void Operation(StringBuilder sb, JsonNode m, string idl, DomValueTypes types, HashSet<string> taken)
    {
        var result = types.CSharp(m["returns"]!.AsString()!, returned: true);
        var method = Pascal(idl);
        if (result is null || RefMembers.Contains(method) || taken.Contains(method))
        {
            return;
        }

        // Each IDL argument list (the overloads too), each union argument once per alternative that crosses the wire,
        // and each list cut before every optional argument: overloads, never optional parameters, so Focus() and
        // Focus(options) cannot be ambiguous.
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var lists = new List<List<JsonNode>> { m["args"]?.Items ?? new List<JsonNode>() };
        lists.AddRange((m["overloads"]?.Items ?? new List<JsonNode>()).Select(o => o.Items));
        foreach (var args in lists.Where(a => !a.Any(x => x["variadic"]?.AsBoolean() == true)))
        {
            var distinct = Prefixes(args).SelectMany(prefix => Expand(prefix, types))
                .Where(parameters => signatures.Add(string.Join(",", parameters.Select(p => p.Type))));
            foreach (var parameters in distinct)
            {
                Overload(sb, m, idl, method, result, parameters);
            }
        }

        if (signatures.Count > 0)
        {
            taken.Add(method);
        }
    }

    // The whole argument list, then the list cut before each optional argument from the end.
    private static IEnumerable<List<JsonNode>> Prefixes(List<JsonNode> args)
    {
        yield return args;
        for (var count = args.Count - 1; count >= 0 && args[count]["optional"]?.AsBoolean() == true; count--)
        {
            yield return args.Take(count).ToList();
        }
    }

    // Every combination of the arguments' types that crosses the wire; none if one argument has no such type.
    private static IEnumerable<List<(string Type, string Name)>> Expand(List<JsonNode> args, DomValueTypes types)
    {
        IEnumerable<List<(string Type, string Name)>> combos = new[] { new List<(string Type, string Name)>() };
        foreach (var a in args)
        {
            var alternatives = DomValueTypes.Alternatives(a["type"]!.AsString()!)
                .Select(t => types.CSharp(t, returned: false)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            var name = Identifier(a["name"]!.AsString()!);
            combos = combos.SelectMany(c => alternatives.Select(t => new List<(string Type, string Name)>(c) { (t, name) })).ToList();
        }

        return combos;
    }

    private static void Overload(StringBuilder sb, JsonNode m, string idl, string method, string result, List<(string Type, string Name)> parameters)
    {
        var isVoid = string.Equals(result, "void", StringComparison.Ordinal);
        DomEmitter.Doc(sb, "        ", $"MDN's <c>{idl}()</c>, called on the live element.", m);
        sb.Append("        public ").Append(isVoid ? "ValueTask" : "ValueTask<" + result + ">").Append(' ').Append(method).Append('(')
            .Append(string.Join(", ", parameters.Select(p => p.Type + " " + p.Name))).Append(") => ElementRefCall.Call");
        if (!isVoid)
        {
            sb.Append('<').Append(result).Append('>');
        }

        sb.Append("(element.Target, \"").Append(idl).Append('"');
        if (parameters.Count == 1)
        {
            sb.Append(", ").Append(parameters[0].Name);
        }
        else if (parameters.Count > 1)
        {
            sb.Append(", new object?[] { ").Append(string.Join(", ", parameters.Select(p => p.Name))).Append(" }");
        }

        sb.AppendLine(");");
        sb.AppendLine();
    }

    private static string Identifier(string name) => CSharpKeywords.Contains(name) ? "@" + name : name;

    internal static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
