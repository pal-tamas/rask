using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

internal static class DomEmitter
{
    // The attributes Rask's Element stores itself (perf-tuned, render-ordered first); every other global
    // attribute MDN defines is generated onto HTMLElement.
    private static readonly HashSet<string> ElementOwnedGlobals = new(StringComparer.Ordinal)
    {
        "id", "class", "style", "title", "lang", "dir", "hidden", "inert", "popover", "contenteditable",
        "spellcheck", "translate", "tabindex", "draggable",
    };

    // Names the DOM gives only because JavaScript reserves the word, and names an inherited Rask member
    // already holds. Anything else that collides fails the build (RASKDOM001) rather than shadowing.
    private static readonly Dictionary<string, string> PropertyAliases = new(StringComparer.Ordinal)
    {
        ["htmlFor"] = "For",
        ["className"] = "Class",
        ["HTMLObjectElement.data"] = "DataUrl",
    };

    private static readonly HashSet<string> ReservedMembers = new(StringComparer.Ordinal)
    {
        "Id", "Class", "Style", "Title", "Data", "Key", "Children", "Ref", "Attributes", "Role", "TabIndex", "Aria",
        "Lang", "Dir", "Hidden", "Inert", "Popover", "ContentEditable", "Spellcheck", "Translate", "Draggable", "TagName",
    };

    // Entries whose tag in PascalCase would shadow something every component can already name.
    private static readonly Dictionary<string, string> EntryAliases = new(StringComparer.Ordinal)
    {
        ["object"] = "HtmlObject", // System.Object
    };

    // SVG tags whose PascalCase entry would shadow System.IO.Path or Rask's Text node take the Svg prefix, as
    // does every SVG tag HTML also has (a, script, style, title).
    private static readonly HashSet<string> SvgPrefixed = new(StringComparer.Ordinal) { "path", "text" };

    // Rask's own security policy, not MDN's: media sources may be inline data:image/video/audio.
    private static readonly HashSet<string> MediaUrlInterfaces = new(StringComparer.Ordinal)
    {
        "HTMLImageElement", "HTMLSourceElement", "HTMLTrackElement", "HTMLMediaElement", "HTMLAudioElement",
        "HTMLVideoElement", "HTMLInputElement", "SVGImageElement", "SVGFEImageElement",
    };

    // URL-typed attributes that hold a LIST of URLs, which a scheme check cannot parse: written as-is.
    private static readonly HashSet<string> UrlLists = new(StringComparer.Ordinal) { "ping", "srcset", "imagesrcset" };

    // Attributes Rask deliberately does not offer. A Rask form submits in-process, so an `action` or
    // `method` would only navigate the page away from the handler the author wrote.
    internal static readonly HashSet<string> Omitted = new(StringComparer.Ordinal) { "HTMLFormElement.action", "HTMLFormElement.method" };

    // What a keyword type's name starts with (DomKeywords.TypeOf).
    private const string KeywordPrefix = "global::Rask.Core.";

    // HTMLElement is also the type of the plain tags (em, section); SVGElement is only ever a base.
    private const string HtmlRoot = "HTMLElement";

    private const string SvgRoot = "SVGElement";

    private const RegexOptions PartialScan = RegexOptions.Compiled | RegexOptions.ExplicitCapture;

    private static readonly TimeSpan PartialScanTimeout = TimeSpan.FromSeconds(1);

    // `class HTMLFormElement<[DynamicallyAccessedMembers(...)] TModel> : HTMLFormElement` included.
    private static readonly Regex TypedControl = new(
        @"class\s+(?<name>HTML\w*Element)\s*<(?:\s*\[[^\]]*\])?\s*(?<param>\w+)\s*>\s*:\s*\k<name>\b", PartialScan, PartialScanTimeout);

    // A hand-written enum in a keyword type's place (src/Rask.Core/InputType.cs), whose body holds no brace.
    private static readonly Regex HandEnum = new(@"public\s+enum\s+(?<name>\w+)\s*\{(?<body>[^}]*)\}", PartialScan, PartialScanTimeout);

    private static readonly Regex PartialClass = new(@"partial\s+class\s+(?<name>(?:HTML|SVG)\w*Element)\b(?!\s*<)", PartialScan, PartialScanTimeout);

    // Each word before the name is taken whole (a space only after a comma, `Dictionary<string, int>`), so a long
    // `public` line is tried once per word, never once per split of it — which a loaded machine stretched past the timeout.
    private static readonly Regex PublicProperty = new(
        @"^\s*public\s+(?:(?>(?:[\w<>\[\]?.:]|,\s*)+)\s+)+?(?<name>\w+)\s*(?:\{|=>)",
        PartialScan | RegexOptions.Multiline, PartialScanTimeout);

    public static IReadOnlyDictionary<string, string> Sources(string snapshotJson)
    {
        return Get(Parse(snapshotJson), "sources").Members.ToDictionary(p => p.Key, p => p.Value.AsString() ?? "", StringComparer.Ordinal);
    }

    public static Partials ReadPartials(IEnumerable<string> sources)
    {
        var result = new Partials();
        foreach (var source in sources)
        {
            if (HandEnum.Match(source) is { Success: true } handEnum)
            {
                result.Enums[handEnum.Groups["name"].Value] = EnumMembers(handEnum.Groups["body"].Value);
                continue;
            }

            string name;
            var typed = TypedControl.Match(source);
            if (typed.Success)
            {
                // A typed control (HTMLInputElement<T> : HTMLInputElement): the MDN type becomes its abstract
                // base, and whatever the typed layer declares, the base does not.
                name = typed.Groups["name"].Value;
                result.Typed[name] = typed.Groups["param"].Value;
            }
            else if (PartialClass.Match(source) is { Success: true } partial)
            {
                name = partial.Groups["name"].Value;
                result.HandWritten.Add(name);
            }
            else
            {
                continue;
            }

            if (!result.Owned.TryGetValue(name, out var members))
            {
                result.Owned[name] = members = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (Match m in PublicProperty.Matches(source))
            {
                members.Add(m.Groups["name"].Value);
            }
        }

        return result;
    }

    // The members of a hand-written enum's body: each name before its `=` or `,`, past its doc comment and attributes.
    private static HashSet<string> EnumMembers(string body)
    {
        var code = string.Join("\n", body.Split('\n').Select(l => l.Trim()).Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("[", StringComparison.Ordinal)));
        return new HashSet<string>(
            code.Split(',').Select(m => m.Split('=')[0].Trim()).Where(m => m.Length > 0),
            StringComparer.Ordinal);
    }

    // `types` is Rask.Web's, which runs Core's pass to learn which value types Core declares; Core passes none. `wasm`
    // receives what Rask.Wasm declares instead of Core: the element-ref members only WebAssembly can run.
    public static IReadOnlyList<KeyValuePair<string, string>> Emit(
        string snapshotJson, Partials partials, DomValueTypes? types = null, List<KeyValuePair<string, string>>? wasm = null)
    {
        var root = Parse(snapshotJson);
        var interfaces = Get(root, "interfaces");
        var tagsByInterface = TagsByInterface(root);
        var htmlTags = new HashSet<string>(
            Get(root, "elements").Items.Where(e => string.Equals(Str(e, "namespace"), "html", StringComparison.Ordinal)).Select(e => Str(e, "tag")!),
            StringComparer.Ordinal);
        var rootOf = DomInterfaces(interfaces, tagsByInterface.Keys);
        var dom = BasesFirst(interfaces, rootOf.Keys);
        var declared = DeclaredAttributes(interfaces, dom);
        types ??= new DomValueTypes(root);
        var keywords = DomKeywords.Read(root, partials, types);

        var files = new List<KeyValuePair<string, string>>();
        // Every property a type has, its bases' included: an attribute a base already writes (SVG's `fill`, which
        // BCD files per shape) is not declared again.
        var props = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var globals = new Dictionary<string, List<(string Attr, string Prop, string Type, string Write)>>(StringComparer.Ordinal);
        foreach (var name in dom)
        {
            var iface = Get(interfaces, name);
            var type = new DomType(name, iface, tagsByInterface.TryGetValue(name, out var t) ? t : new List<JsonNode>(), RootKind(name, rootOf[name]));
            var parent = Str(iface, "parent");
            var inherited = type.Root == Root.None && parent is not null && props.TryGetValue(parent, out var p) ? p : new HashSet<string>(StringComparer.Ordinal);
            type.Source = type.Root == Root.None ? declared[name] : Globals(root, type.Root == Root.Svg).ToList();
            type.Generated = Attributes(type, partials, inherited, keywords);
            type.HasChildren = dom.Any(n => string.Equals(Str(Get(interfaces, n), "parent"), name, StringComparison.Ordinal));

            var owned = partials.Owned.TryGetValue(name, out var o) ? o : Enumerable.Empty<string>();
            props[name] = new HashSet<string>(inherited.Concat(owned).Concat(type.Generated.Select(a => a.Prop)), StringComparer.Ordinal);
            files.Add(new KeyValuePair<string, string>(name + ".g.cs", TypeFile(type, partials, htmlTags)));
            if (type.Root != Root.None)
            {
                globals[name] = type.Generated;
            }
        }

        // What a typed ref may not write: every property a type renders, and what Element itself renders.
        var element = new HashSet<string>(ReservedMembers, StringComparer.Ordinal) { "ClassName" };
        element.UnionWith(props.Where(p => string.Equals(rootOf[p.Key], p.Key, StringComparison.Ordinal)).SelectMany(p => p.Value));
        var rendered = props.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value.Concat(element), StringComparer.Ordinal), StringComparer.Ordinal);
        rendered["Element"] = element;
        DomRefEmitter.Emit(root, dom, rendered, files, types, wasm);

        files.Add(new KeyValuePair<string, string>("Keywords.g.cs", keywords.File()));
        files.Add(new KeyValuePair<string, string>("GlobalAttrs.g.cs", GlobalFields(
            globals.TryGetValue(HtmlRoot, out var html) ? html : new(), globals.TryGetValue(SvgRoot, out var svg) ? svg : new())));
        DomEventEmitter.Emit(root, files);
        AriaEmitter.Emit(root, files);
        return files;
    }

    private enum Root
    {
        None,
        Html,
        Svg,
    }

    // One interface's facts, as TypeFile writes them.
    private sealed class DomType(string name, JsonNode iface, List<JsonNode> tags, Root root)
    {
        public string Name { get; } = name;

        public JsonNode Iface { get; } = iface;

        public List<JsonNode> TagNodes { get; } = tags;

        public Root Root { get; } = root;

        public bool Svg => Root == Root.Svg || string.Equals(Str(Iface, "namespace"), "svg", StringComparison.Ordinal) || Name.StartsWith("SVG", StringComparison.Ordinal);

        public bool HasChildren { get; set; }

        public List<JsonNode> Source { get; set; } = new();

        public List<(string Attr, string Prop, string Type, string Write)> Generated { get; set; } = new();
    }

    private static Root RootKind(string name, string rootName)
    {
        if (!string.Equals(name, rootName, StringComparison.Ordinal))
        {
            return Root.None;
        }

        return string.Equals(name, SvgRoot, StringComparison.Ordinal) ? Root.Svg : Root.Html;
    }

    private static Dictionary<string, List<JsonNode>> TagsByInterface(JsonNode root)
    {
        var tagsByInterface = new Dictionary<string, List<JsonNode>>(StringComparer.Ordinal);
        foreach (var e in Get(root, "elements").Items)
        {
            var i = Str(e, "interface")!;
            if (!tagsByInterface.TryGetValue(i, out var list))
            {
                tagsByInterface[i] = list = new List<JsonNode>();
            }

            list.Add(e);
        }

        return tagsByInterface;
    }

    // Every interface a tag uses and the chain up to its root (HTMLElement or SVGElement), each mapped to that root.
    private static Dictionary<string, string> DomInterfaces(JsonNode interfaces, IEnumerable<string> tagged)
    {
        var rootOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in tagged)
        {
            var chain = new List<string>();
            for (var n = name; n is not null && !string.Equals(n, "Element", StringComparison.Ordinal); n = interfaces[n] is { } d ? Str(d, "parent") : null)
            {
                chain.Add(n);
            }

            foreach (var n in chain)
            {
                rootOf[n] = chain[chain.Count - 1];
            }
        }

        return rootOf;
    }

    // The element interfaces a ref can be typed to, Element and then the rest bases first: what Rask.Web's element-ref
    // members extend.
    internal static List<string> ElementInterfaces(JsonNode root)
    {
        var interfaces = Get(root, "interfaces");
        var names = BasesFirst(interfaces, DomInterfaces(interfaces, TagsByInterface(root).Keys).Keys);
        names.Insert(0, "Element");
        return names;
    }

    private static List<string> BasesFirst(JsonNode interfaces, IEnumerable<string> names) =>
        names.OrderBy(n => Depth(interfaces, n)).ThenBy(n => n, StringComparer.Ordinal).ToList();

    // How far below its root an interface sits, so each is emitted after its bases.
    private static int Depth(JsonNode interfaces, string name)
    {
        var depth = 0;
        for (var n = Str(Get(interfaces, name), "parent"); n is not null && !string.Equals(n, "Element", StringComparison.Ordinal); n = interfaces[n] is { } d ? Str(d, "parent") : null)
        {
            depth++;
        }

        return depth;
    }

    // Each attribute is declared on the interface whose IDL declares it (`on`), once.
    private static Dictionary<string, List<JsonNode>> DeclaredAttributes(JsonNode interfaces, List<string> dom)
    {
        var inDom = new HashSet<string>(dom, StringComparer.Ordinal);
        var declared = dom.ToDictionary(n => n, _ => new List<JsonNode>(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in dom)
        {
            if (Get(interfaces, name)["attributes"] is not { } attrs)
            {
                continue;
            }

            foreach (var a in attrs.Items)
            {
                var on = Str(a, "on") ?? name;
                if (on is HtmlRoot or SvgRoot or "Element" or "Node" || !inDom.Contains(on))
                {
                    on = name;
                }

                if (seen.Add(on + "." + Str(a, "attr")))
                {
                    declared[on].Add(a);
                }
            }
        }

        return declared;
    }

    // The attributes generated onto one interface: all it declares, less what a hand-written partial owns, what a base
    // already has, and what Element itself writes (SVG's <style> has an IDL `title`, and it is the global one).
    private static List<(string Attr, string Prop, string Type, string Write)> Attributes(DomType type, Partials partials, HashSet<string> inherited, DomKeywords keywords)
    {
        var name = type.Name;
        var ownedHere = partials.Owned.TryGetValue(name, out var o) ? new HashSet<string>(o, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        var attributes = new List<(string Attr, string Prop, string Type, string Write)>();
        foreach (var a in type.Source)
        {
            var attr = Str(a, "attr")!;
            var prop = PropertyName(name, a);
            var elementWrites = ReservedMembers.Contains(prop) && string.Equals(prop, attr, StringComparison.OrdinalIgnoreCase);
            if (ownedHere.Contains(prop) || inherited.Contains(prop) || elementWrites || Omitted.Contains(name + "." + attr))
            {
                continue;
            }

            if (ReservedMembers.Contains(prop))
            {
                throw new DomEmitException($"{name}.{attr} maps to '{prop}', which Rask's Element already declares. Add an alias to DomEmitter.PropertyAliases.");
            }

            var csharp = CSharpType(Str(a, "type"));
            if (keywords.TypeOf(a, type.Root == Root.Html) is { } keyword)
            {
                csharp = keyword + "?";
            }
            else if (DomKeywords.IsTrueFalse(a))
            {
                csharp = "bool?";
            }

            attributes.Add((attr, prop, csharp, WriteCall(name, attr, prop, csharp, a, type.Svg)));
            ownedHere.Add(prop);
        }

        return attributes;
    }

    private static string TypeFile(DomType type, Partials partials, HashSet<string> htmlTags)
    {
        var name = type.Name;
        var typed = partials.Typed.TryGetValue(name, out var typeParameter);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Text;");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        Doc(sb, "", TypeSummary(name, type.TagNodes), type.Iface);
        if (!typed)
        {
            Tags(sb, type.TagNodes, htmlTags);
        }

        sb.Append("public ").Append(Modifier(string.Equals(name, HtmlRoot, StringComparison.Ordinal), type.TagNodes.Count == 0 || typed, type.HasChildren))
            .Append("partial class ").Append(name).Append(" : ")
            .AppendLine(type.Root != Root.None ? "global::Rask.Core.Element" : Str(type.Iface, "parent"));
        sb.AppendLine("{");
        Properties(sb, type);
        if (type.Generated.Count > 0 || partials.HandWritten.Contains(name))
        {
            WriteAttributesOverride(sb, type);
        }

        if (type.Root == Root.Svg)
        {
            sb.AppendLine();
            sb.AppendLine("    private protected override GlobalAttrs NewGlobalAttrs() => new SvgGlobalAttrs();");
        }

        sb.AppendLine("}");
        if (typed)
        {
            // The tags go on the typed control, which is what an entry builds.
            sb.AppendLine();
            Tags(sb, type.TagNodes, htmlTags);
            sb.Append("public sealed partial class ").Append(name).Append('<').Append(typeParameter).AppendLine(">;");
        }

        return sb.ToString();
    }

    private static string Modifier(bool concreteRoot, bool isAbstract, bool hasChildren)
    {
        if (concreteRoot)
        {
            return "";
        }

        if (isAbstract)
        {
            return "abstract ";
        }

        return hasChildren ? "" : "sealed ";
    }

    private static void Properties(StringBuilder sb, DomType type)
    {
        foreach (var (attr, prop, csharp, _) in type.Generated)
        {
            var a = type.Source.First(x => string.Equals(Str(x, "attr"), attr, StringComparison.Ordinal));
            Doc(sb, "    ", AttributeSummary(attr, a), a);
            sb.Append("    public ").Append(csharp).Append(' ').Append(prop);
            switch (type.Root)
            {
                case Root.Html:
                    // A global lives on the lazily allocated side object: HTMLElement is the base of every
                    // element, and a field each would cost every node of every live page. Assigning null
                    // to an element that never set one allocates nothing.
                    sb.AppendLine();
                    sb.AppendLine("    {");
                    sb.Append("        get => GlobalAttrsInternal?.").Append(prop).AppendLine(";");
                    sb.Append("        set { if (value is not null || GlobalAttrsInternal is not null) GlobalAttrsForWrite.").Append(prop).AppendLine(" = value; }");
                    sb.AppendLine("    }");
                    break;
                case Root.Svg:
                    // SVG's globals are nearly all presentation attributes: they live on the side object's SVG
                    // subclass, which only an SVG element allocates (NewGlobalAttrs).
                    sb.AppendLine();
                    sb.AppendLine("    {");
                    sb.Append("        get => (GlobalAttrsInternal as SvgGlobalAttrs)?.").Append(prop).AppendLine(";");
                    sb.Append("        set { if (value is not null || GlobalAttrsInternal is not null) ((SvgGlobalAttrs)GlobalAttrsForWrite).").Append(prop).AppendLine(" = value; }");
                    sb.AppendLine("    }");
                    break;
                default:
                    sb.AppendLine(" { get; set; }");
                    break;
            }

            sb.AppendLine();
        }
    }

    private static void WriteAttributesOverride(StringBuilder sb, DomType type)
    {
        sb.AppendLine("    protected override void WriteAttributes(StringBuilder sb)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.WriteAttributes(sb);");
        sb.AppendLine("        WriteOwnedAttributesFirst(sb);");
        if (type.Root != Root.None)
        {
            // One check for the common element, which names no global at all.
            sb.AppendLine(type.Root == Root.Svg ? "        if (GlobalAttrsInternal is SvgGlobalAttrs)" : "        if (GlobalAttrsInternal is not null)");
            sb.AppendLine("        {");
        }

        foreach (var (_, _, _, write) in type.Generated)
        {
            sb.Append(type.Root != Root.None ? "            " : "        ").AppendLine(write);
        }

        if (type.Root != Root.None)
        {
            sb.AppendLine("        }");
        }

        sb.AppendLine("        WriteOwnedAttributes(sb);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    // A hand-written partial writes the attributes it owns in one of these: before the generated ones");
        sb.AppendLine("    // (meta's `property`, which reads first in every Open Graph tag), or after them.");
        sb.AppendLine("    partial void WriteOwnedAttributesFirst(StringBuilder sb);");
        sb.AppendLine();
        sb.AppendLine("    partial void WriteOwnedAttributes(StringBuilder sb);");
    }

    // The side objects' fields for the generated globals (see Component.GlobalAttrs).
    private static string GlobalFields(List<(string Attr, string Prop, string Type, string Write)> globals, List<(string Attr, string Prop, string Type, string Write)> svg)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("public abstract partial class Component");
        sb.AppendLine("{");
        sb.AppendLine("    internal partial class GlobalAttrs");
        sb.AppendLine("    {");
        foreach (var (_, prop, type, _) in globals)
        {
            sb.Append("        public ").Append(type).Append(' ').Append(prop).AppendLine(";");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    // An SVG element's side object: HTML's globals, and SVG's rarer presentation attributes.");
        sb.AppendLine("    internal sealed class SvgGlobalAttrs : GlobalAttrs");
        sb.AppendLine("    {");
        foreach (var (_, prop, type, _) in svg)
        {
            sb.Append("        public ").Append(type).Append(' ').Append(prop).AppendLine(";");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Tags(StringBuilder sb, List<JsonNode> tags, HashSet<string> htmlTags)
    {
        foreach (var tag in tags)
        {
            var tagName = Str(tag, "tag")!;
            sb.Append("[global::Rask.Core.Tag(\"").Append(tagName).Append('"');
            var svg = string.Equals(Str(tag, "namespace"), "svg", StringComparison.Ordinal);
            if (svg && (htmlTags.Contains(tagName) || SvgPrefixed.Contains(tagName)))
            {
                sb.Append(", Entry = \"Svg").Append(Pascal(tagName)).Append('"');
            }
            else if (!svg && EntryAliases.TryGetValue(tagName, out var entry))
            {
                sb.Append(", Entry = \"").Append(entry).Append('"');
            }

            sb.AppendLine(")]");
        }
    }

    // HTML's globals that an IDL attribute reflects; every SVG global, since SVG's are presentation attributes:
    // CSS properties, not IDL ones.
    private static IEnumerable<JsonNode> Globals(JsonNode root, bool svg) =>
        Get(Get(root, "globalAttributes"), svg ? "svg" : "html").Items
            .Where(a => !ElementOwnedGlobals.Contains(Str(a, "attr")!) && (svg || Str(a, "property") is not null));

    private static string PropertyName(string iface, JsonNode a) => PropertyName(iface, Str(a, "attr")!, Str(a, "property"), Str(a, "type"));

    // A content attribute's C# name: its IDL attribute's, PascalCased, or the attribute's own words where no IDL one reflects it.
    internal static string PropertyName(string iface, string attr, string? idl, string? idlType)
    {
        if (PropertyAliases.TryGetValue(iface + "." + attr, out var scoped))
        {
            return scoped;
        }

        if (idl is null)
        {
            return string.Concat(attr.Split('-').Select(Pascal));
        }

        if (PropertyAliases.TryGetValue(idl, out var alias))
        {
            return alias;
        }

        // An IDL attribute that holds an element or a list of them (`commandForElement`, `ariaLabelledByElements`) is
        // written from markup as the id(s) it points at, so the property is named for the attribute it sets:
        // CommandFor, AriaLabelledBy.
        if (!IsPlain(idlType))
        {
            idl = StripSuffix(StripSuffix(idl, "Elements"), "Element");
        }

        return Pascal(idl);
    }

    private static string StripSuffix(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length ? name.Substring(0, name.Length - suffix.Length) : name;

    private static bool IsPlain(string? idlType) =>
        idlType is "DOMString" or "USVString" or "DOMString?" or "boolean" or "long" or "unsigned long" or "double" or "unrestricted double";

    // CLS-compliant C# for each IDL type a content attribute carries; anything richer is the attribute's text.
    private static string CSharpType(string? idlType) => idlType switch
    {
        "boolean" => "bool?",
        "long" or "unsigned long" or "short" or "unsigned short" => "int?",
        "double" or "unrestricted double" => "double?",
        _ => "string?",
    };

    private static string WriteCall(string iface, string attr, string prop, string type, JsonNode a, bool svg)
    {
        var name = Literal(attr);
        switch (type)
        {
            case "bool?":
                return DomKeywords.BooleanKeywords(a) is { } kw
                    ? $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)} ? {Literal(kw.On)} : {Literal(kw.Off)});"
                    : $"if ({prop} is true) AppendAttr(sb, {name}, null);";
            case var keyword when keyword.StartsWith(KeywordPrefix, StringComparison.Ordinal):
                return $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, global::Rask.Core.KeywordText.Of({Local(prop)}));";
            case "int?":
                return $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)});";
            case "double?":
                return $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)}.ToString(global::System.Globalization.CultureInfo.InvariantCulture));";
        }

        // SVG's href is an SVGAnimatedString in the IDL, and a URL all the same.
        if ((a["url"]?.AsBoolean() == true || (svg && string.Equals(attr, "href", StringComparison.Ordinal))) && !UrlLists.Contains(attr))
        {
            var helper = MediaUrlInterfaces.Contains(iface) && attr is "src" or "poster" or "href" ? "AppendMediaUrlAttr" : "AppendUrlAttr";
            return $"if ({prop} is not null) {helper}(sb, {name}, {prop});";
        }

        return $"if ({prop} is not null) AppendAttr(sb, {name}, {prop});";
    }

    private static string TypeSummary(string name, List<JsonNode> tags) => tags.Count switch
    {
        0 => $"The DOM's <c>{name}</c>: what its elements share. No tag builds it directly.",
        _ => $"The {string.Join(", ", tags.Select(t => $"<c>&lt;{Str(t, "tag")}&gt;</c>"))} element{(tags.Count > 1 ? "s" : "")}, as the DOM's <c>{name}</c>.",
    };

    private static string AttributeSummary(string attr, JsonNode a)
    {
        var text = new StringBuilder($"The <c>{attr}</c> attribute");
        if (a["tags"] is { } tags)
        {
            text.Append(" (on ").Append(string.Join(", ", tags.Items.Select(t => $"<c>&lt;{t.AsString()}&gt;</c>"))).Append(" only)");
        }

        text.Append('.');
        if (a["reflectDefault"] is { Kind: JsonKind.String or JsonKind.Number } d && d.Text is { Length: > 0 } dflt)
        {
            text.Append(" Default ").Append(dflt);
            text.Append(a["reflectRange"] is { Kind: JsonKind.Array } r && r.Items.Count == 2
                ? $", range {r.Items[0].Text}–{r.Items[1].Text}."
                : ".");
        }

        return text.ToString();
    }

    // Written from data: what the member is, its support line, and links. Never MDN's prose (CC-BY-SA).
    internal static void Doc(StringBuilder sb, string indent, string summary, JsonNode data)
    {
        sb.Append(indent).Append("/// <summary>").Append(summary).AppendLine("</summary>");
        var remarks = new List<string>();
        if (data["experimental"]?.AsBoolean() == true)
        {
            remarks.Add("<b>Experimental.</b>");
        }

        // On no standards track: scripts/mdn/refresh.mjs admits these by name only (NON_STANDARD).
        if (data["nonStandard"]?.AsBoolean() == true)
        {
            remarks.Add("<b>Non-standard.</b>");
        }

        if (data["secure"]?.AsBoolean() == true)
        {
            remarks.Add("Secure contexts only: an HTTPS page, or localhost.");
        }

        if (data["support"] is { Kind: JsonKind.Object } support)
        {
            // A web API ships in one engine at least (WebUSB is Chromium's alone): say so before the versions.
            var engines = support.Members.Select(p => EngineName(p.Key)).Distinct(StringComparer.Ordinal).ToList();
            if (engines.Count == 1)
            {
                remarks.Add($"<b>{engines[0]} only.</b>");
            }

            var line = string.Join(" · ", support.Members.Select(p => $"{BrowserName(p.Key)} {p.Value.AsString()}"));
            if (line.Length > 0)
            {
                remarks.Add(line + ".");
            }
        }

        if (Str(data, "mdn") is { } mdn)
        {
            remarks.Add($"<see href=\"{mdn}\">MDN</see>");
        }

        if (Str(data, "spec") is { } spec)
        {
            remarks.Add($"<see href=\"{spec}\">Spec</see>");
        }

        if (remarks.Count > 0)
        {
            sb.Append(indent).Append("/// <remarks>").Append(string.Join(" ", remarks)).AppendLine("</remarks>");
        }
    }

    // A member's doc comment, then its support as data: an analyzer cannot read a referenced assembly's doc comments,
    // so RASK098 (<RaskBrowserTargets>) reads this attribute off the symbol instead. One version per engine.
    internal static void Member(StringBuilder sb, string indent, string summary, JsonNode data)
    {
        Doc(sb, indent, summary, data);
        if (data["support"] is not { Kind: JsonKind.Object } support || support.Members.Count == 0)
        {
            return;
        }

        var versions = support.Members
            .GroupBy(p => EngineProperty(p.Key), StringComparer.Ordinal)
            .Select(g => $"{g.Key} = {Literal(g.First().Value.AsString() ?? "")}");
        sb.Append(indent).Append("[global::Rask.Core.BrowserSupport(").Append(string.Join(", ", versions)).AppendLine(")]");
    }

    private static string EngineProperty(string id) => id switch
    {
        "chrome" or "chrome_android" => "Chrome",
        "firefox" or "firefox_android" => "Firefox",
        "safari" or "safari_ios" => "Safari",
        _ => throw new DomEmitException($"the snapshot names a browser `{id}` no engine is known for"),
    };

    internal static string BrowserName(string id) => id switch
    {
        "chrome" => "Chrome",
        "chrome_android" => "Chrome Android",
        "firefox" => "Firefox",
        "firefox_android" => "Firefox Android",
        "safari" => "Safari",
        "safari_ios" => "Safari iOS",
        _ => id,
    };

    private static string EngineName(string id) => id switch
    {
        "chrome" or "chrome_android" => "Chromium",
        "firefox" or "firefox_android" => "Firefox",
        "safari" or "safari_ios" => "Safari",
        _ => id,
    };

    internal static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string Local(string prop) => "@" + char.ToLowerInvariant(prop[0]) + prop.Substring(1);

    internal static string Literal(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    internal static string? Str(JsonNode e, string name) => e[name]?.AsString();

    internal static JsonNode Get(JsonNode e, string name) =>
        e[name] ?? throw new DomEmitException($"the snapshot has no `{name}` where one was expected");

    internal static JsonNode Parse(string json)
    {
        var result = JsonLite.Parse(json);
        return result.Root ?? throw new DomEmitException($"not JSON ({result.Defect} at {result.Line}:{result.Column})");
    }
}
