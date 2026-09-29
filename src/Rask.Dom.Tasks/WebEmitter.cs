using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// Rask.Web: every interface a window exposes that ships, generated from the snapshot. Each becomes a proxy in
// Rask.Web.Types over a lazy chain (a JsChain, run in one round trip when awaited), and window's own members and the
// objects it holds become the globals in Rask.Web: `await Navigator.Clipboard.WriteText("hi")`.
internal static class WebEmitter
{
    private const string TypesNs = "global::Rask.Web.Types.";

    // What JsObject answers itself, and what a global's own plumbing is named.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "Chain", "IsSupported", "DisposeAsync", "With", "Keep", "Equals", "GetHashCode", "ToString", "GetType", "Instance",
    };

    // Members that would tear down the page Rask renders, and writes to what its render owns ("=": the write only):
    // the page's Head sets the title, and the next render would undo any other.
    private static readonly HashSet<string> Denied = new(StringComparer.Ordinal)
    {
        "Document.write", "Document.writeln", "Document.open", "Document.close", "Document.title=",
    };

    public static IReadOnlyList<KeyValuePair<string, string>> Emit(string snapshotJson)
    {
        var root = DomEmitter.Parse(snapshotJson);

        // Core's element pass first: the value types it declares are Core's, and this names them rather than again.
        var types = new DomValueTypes(root, TypesNs);
        DomEmitter.Emit(snapshotJson, new Partials(), types);
        types.MarkExternal();

        var interfaces = root["interfaces"]!;
        var proxies = Proxies(root, types);
        var members = new Dictionary<string, List<WebMember>>(StringComparer.Ordinal);
        foreach (var name in proxies.OrderBy(n => Depth(interfaces, n)).ThenBy(n => n, StringComparer.Ordinal))
        {
            var taken = new HashSet<string>(Reserved, StringComparer.Ordinal) { name };
            for (var b = Base(interfaces, proxies, name); b is not null; b = Base(interfaces, proxies, b))
            {
                taken.UnionWith(members[b].Select(m => m.Name));
            }

            members[name] = WebMember.Of(name, interfaces[name]!, types, proxies, taken, Denied);
        }

        var files = new List<KeyValuePair<string, string>>();
        foreach (var name in proxies.OrderBy(n => n, StringComparer.Ordinal))
        {
            var hasChildren = proxies.Any(p => string.Equals(Base(interfaces, proxies, p), name, StringComparison.Ordinal));
            files.Add(new KeyValuePair<string, string>(name + ".g.cs", Proxy(name, interfaces[name]!, Base(interfaces, proxies, name), hasChildren, members[name])));
        }

        files.Add(new KeyValuePair<string, string>("Globals.g.cs", Globals(interfaces, proxies, members)));
        files.Add(new KeyValuePair<string, string>("WebValues.g.cs", types.Declarations("Rask.Web.Types", "RaskWebJsonContext")));
        return files;
    }

    // The interfaces that become live proxies: the web ones that are not values, not DOM nodes, since Rask owns the DOM,
    // and not events, which Core models as the payload a handler gets. Document stays, for what it knows beyond its tree.
    private static HashSet<string> Proxies(JsonNode root, DomValueTypes types)
    {
        var interfaces = root["interfaces"]!;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in (root["web"]?.Items ?? new List<JsonNode>()).Select(n => n.AsString()!))
        {
            var node = Derives(interfaces, name, "Node") && !string.Equals(name, "Document", StringComparison.Ordinal);
            if (!node && !Derives(interfaces, name, "Event") && !types.IsValue(name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static bool Derives(JsonNode interfaces, string name, string ancestor)
    {
        for (var n = name; n is not null; n = interfaces[n]?["parent"]?.AsString())
        {
            if (string.Equals(n, ancestor, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // The nearest ancestor that is a proxy too: Document's is EventTarget, past Node.
    private static string? Base(JsonNode interfaces, HashSet<string> proxies, string name)
    {
        for (var n = interfaces[name]?["parent"]?.AsString(); n is not null; n = interfaces[n]?["parent"]?.AsString())
        {
            if (proxies.Contains(n))
            {
                return n;
            }
        }

        return null;
    }

    private static int Depth(JsonNode interfaces, string name)
    {
        var depth = 0;
        for (var n = interfaces[name]?["parent"]?.AsString(); n is not null; n = interfaces[n]?["parent"]?.AsString())
        {
            depth++;
        }

        return depth;
    }

    private static string Proxy(string name, JsonNode iface, string? baseName, bool hasChildren, List<WebMember> members)
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Web.Types;");
        sb.AppendLine();
        DomEmitter.Doc(sb, "", $"MDN's <c>{name}</c>, a live object in the browser: each member runs in one round trip when awaited.", iface);
        sb.Append("public ").Append(hasChildren ? "" : "sealed ").Append("class ").Append(name).Append(" : ")
            .AppendLine(baseName is null ? "global::Rask.Web.JsObject" : TypesNs + baseName);
        sb.AppendLine("{");
        sb.Append("    internal ").Append(name).AppendLine("(global::Rask.Web.JsChain chain)");
        sb.AppendLine("        : base(chain)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.Append("    private protected override global::Rask.Web.JsObject With(global::Rask.Web.JsChain chain) => new ").Append(name).AppendLine("(chain);");
        foreach (var m in members)
        {
            sb.AppendLine();
            m.WriteInstance(sb);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Window's members as statics on `Window`, and each object window holds (navigator, localStorage) as a global of
    // its own, named for the property: Navigator, LocalStorage.
    private static string Globals(JsonNode interfaces, HashSet<string> proxies, Dictionary<string, List<WebMember>> members)
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Web;");
        sb.AppendLine();
        if (proxies.Contains("Window"))
        {
            Global(sb, "Window", "window", "Window", "global::Rask.Web.JsChain.Window", interfaces, proxies, members);
        }

        foreach (var a in interfaces["Window"]?["members"]?.Items ?? new List<JsonNode>())
        {
            var type = a["type"]?.AsString()?.TrimEnd('?');
            var name = DomRefEmitter.Pascal(a["name"]!.AsString()!);
            if (string.Equals(a["kind"]?.AsString(), "attribute", StringComparison.Ordinal) && type is not null && proxies.Contains(type)
                && !string.Equals(name, "Window", StringComparison.Ordinal))
            {
                var idl = a["name"]!.AsString()!;
                Global(sb, name, idl, type, $"global::Rask.Web.JsChain.Window.Get(\"{idl}\")", interfaces, proxies, members);
            }
        }

        return sb.ToString();
    }

    private static void Global(
        StringBuilder sb, string name, string idl, string type, string chain, JsonNode interfaces, HashSet<string> proxies,
        Dictionary<string, List<WebMember>> members)
    {
        DomEmitter.Doc(sb, "", $"The browser's <c>{idl}</c> (MDN's <c>{type}</c>): its members, run in one round trip each.", interfaces[type]!);
        sb.Append("public static class ").AppendLine(name);
        sb.AppendLine("{");
        sb.Append("    private static ").Append(TypesNs).Append(type).Append(" Instance => new(").Append(chain).AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Whether this browser has it.</summary>");
        sb.AppendLine("    public static ValueTask<bool> IsSupported => Instance.IsSupported;");
        var seen = new HashSet<string>(StringComparer.Ordinal) { "IsSupported" };
        for (var t = type; t is not null; t = Base(interfaces, proxies, t))
        {
            foreach (var m in members[t].Where(m => seen.Add(m.Signature)))
            {
                sb.AppendLine();
                m.WriteStatic(sb);
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }
}
