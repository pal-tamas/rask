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
        var payloads = new WebPayloads(root, types);
        var members = new Dictionary<string, List<WebMember>>(StringComparer.Ordinal);
        var extras = new Dictionary<string, List<WebMember>>(StringComparer.Ordinal);
        foreach (var name in proxies.OrderBy(n => Depth(interfaces, n)).ThenBy(n => n, StringComparer.Ordinal))
        {
            var taken = new HashSet<string>(Reserved, StringComparer.Ordinal) { name };
            var creates = new HashSet<string>(StringComparer.Ordinal);
            for (var b = Base(interfaces, proxies, name); b is not null; b = Base(interfaces, proxies, b))
            {
                taken.UnionWith(members[b].Select(m => m.Name));
                taken.UnionWith(extras[b].Where(m => !string.Equals(m.Name, "Create", StringComparison.Ordinal)).Select(m => m.Name));
                creates.UnionWith(extras[b].Where(m => string.Equals(m.Name, "Create", StringComparison.Ordinal)).Select(m => m.ParameterTypes));
            }

            members[name] = WebMember.Of(name, interfaces[name]!, types, proxies, taken, Denied, root["callbacks"]);
            members[name].AddRange(WebMember.EventsOf(interfaces[name]!, payloads, taken));
            extras[name] = WebMember.StaticsOf(name, interfaces[name]!, types, proxies, taken);
            if (!taken.Contains("Create"))
            {
                extras[name].AddRange(WebMember.ConstructorsOf(name, interfaces[name]!, types, creates));
            }
        }

        var files = new List<KeyValuePair<string, string>>();
        foreach (var name in proxies.OrderBy(n => n, StringComparer.Ordinal))
        {
            var hasChildren = proxies.Any(p => string.Equals(Base(interfaces, proxies, p), name, StringComparison.Ordinal));
            files.Add(new KeyValuePair<string, string>(name + ".g.cs", Proxy(name, interfaces[name]!, Base(interfaces, proxies, name), hasChildren, members[name])));
        }

        files.Add(new KeyValuePair<string, string>("WebEvents.g.cs", payloads.Declarations()));
        files.Add(new KeyValuePair<string, string>("Globals.g.cs", Globals(new Model(interfaces, proxies, members, extras))));
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

    // What the globals are written from: every proxy, its instance members, and its statics and constructors.
    private sealed class Model(JsonNode interfaces, HashSet<string> proxies, Dictionary<string, List<WebMember>> members, Dictionary<string, List<WebMember>> extras)
    {
        public JsonNode Interfaces { get; } = interfaces;

        public HashSet<string> ProxyNames { get; } = proxies;

        public Dictionary<string, List<WebMember>> Members { get; } = members;

        public Dictionary<string, List<WebMember>> Extras { get; } = extras;
    }

    // Everything in Rask.Web, the namespace a file imports: Window's members as statics on `Window`, each object window
    // holds (navigator, localStorage) as a global named for the property, and each interface's statics and
    // constructors on a class of its name — URL.CanParse(…), BroadcastChannel.Create(…) — merged into the global where
    // one has that name (Document).
    private static string Globals(Model model)
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Web;");
        sb.AppendLine();
        var written = new HashSet<string>(StringComparer.Ordinal);
        if (model.ProxyNames.Contains("Window"))
        {
            Global(sb, model, "Window", "window", "Window", "global::Rask.Web.JsChain.Window");
            written.Add("Window");
        }

        foreach (var a in model.Interfaces["Window"]?["members"]?.Items ?? new List<JsonNode>())
        {
            var type = a["type"]?.AsString()?.TrimEnd('?');
            var name = DomRefEmitter.Pascal(a["name"]!.AsString()!);
            if (string.Equals(a["kind"]?.AsString(), "attribute", StringComparison.Ordinal) && type is not null && model.ProxyNames.Contains(type)
                && written.Add(name))
            {
                var idl = a["name"]!.AsString()!;
                Global(sb, model, name, idl, type, $"global::Rask.Web.JsChain.Window.Get(\"{idl}\")");
            }
        }

        foreach (var name in model.Extras.Keys.Where(n => model.Extras[n].Count > 0 && !written.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
        {
            DomEmitter.Doc(sb, "", $"MDN's <c>{name}</c> class: its constructors, as Create, and its static members.", model.Interfaces[name]!);
            sb.Append("public static class ").AppendLine(name);
            sb.AppendLine("{");
            Facade(sb, model.Extras[name], new HashSet<string>(StringComparer.Ordinal));
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void Global(StringBuilder sb, Model model, string name, string idl, string type, string chain)
    {
        DomEmitter.Doc(sb, "", $"The browser's <c>{idl}</c> (MDN's <c>{type}</c>): its members, run in one round trip each.", model.Interfaces[type]!);
        sb.Append("public static class ").AppendLine(name);
        sb.AppendLine("{");
        sb.Append("    private static ").Append(TypesNs).Append(type).Append(" Instance => new(").Append(chain).AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Whether this browser has it.</summary>");
        sb.AppendLine("    public static ValueTask<bool> IsSupported => Instance.IsSupported;");
        var seen = new HashSet<string>(StringComparer.Ordinal) { "IsSupported" };
        for (var t = type; t is not null; t = Base(model.Interfaces, model.ProxyNames, t))
        {
            foreach (var m in model.Members[t].Where(m => seen.Add(m.Signature)))
            {
                sb.AppendLine();
                m.WriteStatic(sb);
            }
        }

        // The interface of the global's name brings its statics and constructors (Document.Create()).
        if (model.Extras.TryGetValue(name, out var extras))
        {
            Facade(sb, extras, new HashSet<string>(seen.Select(x => x.Split('(')[0]), StringComparer.Ordinal));
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // Statics and constructors, less any whose name a delegated member of the same class already holds.
    private static void Facade(StringBuilder sb, List<WebMember> extras, HashSet<string> taken)
    {
        foreach (var m in extras.Where(m => !taken.Contains(m.Name)))
        {
            sb.AppendLine();
            m.WriteFacade(sb);
        }
    }
}
