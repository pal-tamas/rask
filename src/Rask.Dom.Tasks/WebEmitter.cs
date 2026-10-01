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

    // Rask.Web's files, or with `wasm` Rask.Wasm's: the per-frame families whole, and the members only WebAssembly can
    // run (WebHost) as extension members of the proxies and globals Rask.Web declares, beside the element refs' own.
    public static IReadOnlyList<KeyValuePair<string, string>> Emit(string snapshotJson, bool wasm = false)
    {
        var root = DomEmitter.Parse(snapshotJson);

        // Core's element pass first: the value types it declares are Core's, and this names them rather than again.
        var types = new DomValueTypes(root, TypesNs);
        DomEmitter.Emit(snapshotJson, new Partials(), types);
        types.MarkExternal();

        // The element-ref members only WebAssembly runs, from a pass of Core's own, which names Core's types as Core does.
        var wasmRefs = new List<KeyValuePair<string, string>>();
        if (wasm)
        {
            DomEmitter.Emit(snapshotJson, new Partials(), wasm: wasmRefs);
        }

        var payloads = new WebPayloads(root, types);
        var model = Build(root, types, payloads);
        var files = new List<KeyValuePair<string, string>>();
        foreach (var name in model.ProxyNames.Where(n => WebHost.IsWasmInterface(n) == wasm).OrderBy(n => n, StringComparer.Ordinal))
        {
            var hasChildren = model.ProxyNames.Any(p => string.Equals(Base(model.Interfaces, model.ProxyNames, p), name, StringComparison.Ordinal));
            var members = model.Members[name].Where(m => wasm || !m.Wasm).ToList();
            files.Add(new KeyValuePair<string, string>(
                name + ".g.cs", Proxy(name, model.Interfaces[name]!, Base(model.Interfaces, model.ProxyNames, name), hasChildren, members)));
        }

        if (wasm)
        {
            files.Add(new KeyValuePair<string, string>("WasmMembers.g.cs", WasmMembers(model)));
            files.AddRange(wasmRefs);
            return files;
        }

        files.Add(new KeyValuePair<string, string>("WebEvents.g.cs", payloads.Declarations()));
        files.Add(new KeyValuePair<string, string>("Globals.g.cs", Globals(model)));
        files.Add(new KeyValuePair<string, string>("WebValues.g.cs", types.Declarations("Rask.Web.Types", "RaskWebJsonContext")));
        return files;
    }

    // Every proxy's members, statics and constructors, bases first so a derived one never declares a name again.
    private static Model Build(JsonNode root, DomValueTypes types, WebPayloads payloads)
    {
        var interfaces = root["interfaces"]!;
        var proxies = Proxies(root, types);
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

        return new Model(interfaces, proxies, members, extras);
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
    // one has that name (Document). Each is a sealed class, not a static one, so Rask.Wasm can extend it.
    private static string Globals(Model model)
    {
        var sb = Open("Rask.Web");
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, idl, type, chain) in GlobalList(model))
        {
            Global(sb, model, name, idl, type, chain);
            written.Add(name);
        }

        foreach (var name in model.Extras.Keys.Where(n => model.Extras[n].Count > 0 && !written.Contains(n) && !WebHost.IsWasmInterface(n))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            Facade(sb, model, name, model.Extras[name].Where(m => !m.Wasm));
        }

        return sb.ToString();
    }

    // The globals: window itself, and each object it holds by a property.
    private static List<(string Name, string Idl, string Type, string Chain)> GlobalList(Model model)
    {
        var result = new List<(string Name, string Idl, string Type, string Chain)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (model.ProxyNames.Contains("Window") && names.Add("Window"))
        {
            result.Add(("Window", "window", "Window", "global::Rask.Web.JsChain.Window"));
        }

        foreach (var a in model.Interfaces["Window"]?["members"]?.Items ?? new List<JsonNode>())
        {
            var type = a["type"]?.AsString()?.TrimEnd('?');
            var name = DomRefEmitter.Pascal(a["name"]!.AsString()!);
            if (string.Equals(a["kind"]?.AsString(), "attribute", StringComparison.Ordinal) && type is not null && model.ProxyNames.Contains(type)
                && names.Add(name))
            {
                if (WebHost.IsWasmInterface(type))
                {
                    throw new DomEmitException($"window.{a["name"]!.AsString()} holds a {type}, which only WebAssembly runs: give Rask.Wasm its global.");
                }

                var idl = a["name"]!.AsString()!;
                result.Add((name, idl, type, $"global::Rask.Web.JsChain.Window.Get(\"{idl}\")"));
            }
        }

        return result;
    }

    private static StringBuilder Open(string ns)
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.Append("namespace ").Append(ns).AppendLine(";");
        sb.AppendLine();
        return sb;
    }

    private static void Global(StringBuilder sb, Model model, string name, string idl, string type, string chain)
    {
        DomEmitter.Doc(sb, "", $"The browser's <c>{idl}</c> (MDN's <c>{type}</c>): its members, run in one round trip each.", model.Interfaces[type]!);
        OpenClass(sb, name);
        sb.Append("    internal static ").Append(TypesNs).Append(type).Append(" Instance => new(").Append(chain).AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Whether this browser has it.</summary>");
        sb.AppendLine("    public static ValueTask<bool> IsSupported => Instance.IsSupported;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>A test's stand-in for it, for the rest of the test's flow, until disposed of.</summary>");
        sb.Append("    public static global::Rask.Web.WebFake<").Append(TypesNs).Append(type).AppendLine("> Fake() => Instance.Fake();");
        var members = GlobalMembers(model, type);
        foreach (var m in members.Where(m => !m.Wasm))
        {
            sb.AppendLine();
            m.WriteStatic(sb);
        }

        // The interface of the global's name brings its statics and constructors (Document.Create()).
        if (model.Extras.TryGetValue(name, out var extras))
        {
            WriteFacade(sb, FacadeOf(extras, members).Where(m => !m.Wasm));
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void OpenClass(StringBuilder sb, string name)
    {
        sb.Append("public sealed class ").AppendLine(name);
        sb.AppendLine("{");
        sb.Append("    private ").Append(name).AppendLine("()");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    // A global's instance members, its type's and its bases', each overload once.
    private static List<WebMember> GlobalMembers(Model model, string type)
    {
        var result = new List<WebMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { "IsSupported", "Fake" };
        for (var t = type; t is not null; t = Base(model.Interfaces, model.ProxyNames, t))
        {
            result.AddRange(model.Members[t].Where(m => seen.Add(m.Signature)));
        }

        return result;
    }

    // Statics and constructors, less any whose name a delegated member of the same class already holds.
    private static IEnumerable<WebMember> FacadeOf(List<WebMember> extras, List<WebMember> members)
    {
        var taken = new HashSet<string>(members.Select(m => m.Name), StringComparer.Ordinal) { "IsSupported", "Fake" };
        return extras.Where(m => !taken.Contains(m.Name));
    }

    private static void Facade(StringBuilder sb, Model model, string name, IEnumerable<WebMember> extras)
    {
        DomEmitter.Doc(sb, "", $"MDN's <c>{name}</c> class: its constructors, as Create, and its static members.", model.Interfaces[name]!);
        OpenClass(sb, name);
        WriteFacade(sb, extras);
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void WriteFacade(StringBuilder sb, IEnumerable<WebMember> extras)
    {
        foreach (var m in extras)
        {
            sb.AppendLine();
            m.WriteFacade(sb);
        }
    }

    // Rask.Wasm's side: the per-frame families' own classes, and in one static class the members only WebAssembly runs,
    // as extension members: instance ones on Rask.Web's proxies, static ones on its globals and classes.
    private static string WasmMembers(Model model)
    {
        var sb = Open("Rask.Web");
        foreach (var name in model.Extras.Keys.Where(n => model.Extras[n].Count > 0 && WebHost.IsWasmInterface(n)).OrderBy(n => n, StringComparer.Ordinal))
        {
            Facade(sb, model, name, model.Extras[name]);
        }

        sb.AppendLine("/// <summary>The web API members only WebAssembly can run: each needs the user's click in progress, generated from MDN.</summary>");
        sb.AppendLine("public static class WasmMembers");
        sb.AppendLine("{");
        foreach (var name in model.ProxyNames.Where(n => !WebHost.IsWasmInterface(n)).OrderBy(n => n, StringComparer.Ordinal))
        {
            Extension(sb, TypesNs + name + " self", model.Members[name].Where(m => m.Wasm), m => m.WriteExtension(sb, "self"));
        }

        var globals = GlobalList(model);
        foreach (var (name, _, type, _) in globals)
        {
            var members = GlobalMembers(model, type);
            var extras = model.Extras.TryGetValue(name, out var e) ? FacadeOf(e, members) : Enumerable.Empty<WebMember>();
            Extension(sb, "global::Rask.Web." + name, members.Where(m => m.Wasm), m => m.WriteStatic(sb, "        ", $"global::Rask.Web.{name}.Instance"));
            Extension(sb, "global::Rask.Web." + name, extras.Where(m => m.Wasm), m => m.WriteFacade(sb, "        "));
        }

        foreach (var name in model.Extras.Keys.Where(n => !WebHost.IsWasmInterface(n) && !globals.Any(g => string.Equals(g.Name, n, StringComparison.Ordinal)))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            Extension(sb, "global::Rask.Web." + name, model.Extras[name].Where(m => m.Wasm), m => m.WriteFacade(sb, "        "));
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Extension(StringBuilder sb, string receiver, IEnumerable<WebMember> members, Action<WebMember> write)
    {
        var list = members.ToList();
        if (list.Count == 0)
        {
            return;
        }

        sb.Append("    extension(").Append(receiver).AppendLine(")");
        sb.AppendLine("    {");
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            write(list[i]);
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }
}
