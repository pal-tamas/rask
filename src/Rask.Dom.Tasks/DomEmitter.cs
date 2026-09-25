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

    // Rask's own security policy, not MDN's: media sources may be inline data:image/video/audio.
    private static readonly HashSet<string> MediaUrlInterfaces = new(StringComparer.Ordinal)
    {
        "HTMLImageElement", "HTMLSourceElement", "HTMLTrackElement", "HTMLMediaElement", "HTMLAudioElement",
        "HTMLVideoElement", "HTMLInputElement",
    };

    // URL-typed attributes that hold a LIST of URLs, which a scheme check cannot parse: written as-is.
    private static readonly HashSet<string> UrlLists = new(StringComparer.Ordinal) { "ping", "srcset", "imagesrcset" };

    // Attributes Rask deliberately does not offer. A Rask form submits in-process, so an `action` or
    // `method` would only navigate the page away from the handler the author wrote.
    private static readonly HashSet<string> Omitted = new(StringComparer.Ordinal) { "HTMLFormElement.action", "HTMLFormElement.method" };

    // Boolean IDL attributes whose content attribute is a keyword pair rather than present/absent.
    private static readonly Dictionary<string, (string On, string Off)> KeywordBooleans = new(StringComparer.Ordinal)
    {
        ["autocorrect"] = ("on", "off"),
    };

    private const string RootInterface = "HTMLElement";

    private const RegexOptions PartialScan = RegexOptions.Compiled | RegexOptions.ExplicitCapture;

    private static readonly TimeSpan PartialScanTimeout = TimeSpan.FromSeconds(1);

    // `class HTMLFormElement<[DynamicallyAccessedMembers(...)] TModel> : HTMLFormElement` included.
    private static readonly Regex TypedControl = new(
        @"class\s+(?<name>HTML\w*Element)\s*<(?:\s*\[[^\]]*\])?\s*(?<param>\w+)\s*>\s*:\s*\k<name>\b", PartialScan, PartialScanTimeout);

    private static readonly Regex PartialClass = new(@"partial\s+class\s+(?<name>HTML\w*Element)\b(?!\s*<)", PartialScan, PartialScanTimeout);

    private static readonly Regex PublicProperty = new(
        @"^\s*public\s+(?:new\s+|override\s+|virtual\s+|required\s+)*[\w<>\[\]?.,: ]+?\s+(?<name>\w+)\s*(?:\{|=>)",
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

    public static IReadOnlyList<KeyValuePair<string, string>> Emit(string snapshotJson, Partials partials)
    {
        var root = Parse(snapshotJson);
        var interfaces = Get(root, "interfaces");
        var tagsByInterface = TagsByInterface(root);
        var html = HtmlInterfaces(interfaces, tagsByInterface.Keys);
        var declared = DeclaredAttributes(interfaces, html);

        var files = new List<KeyValuePair<string, string>>();
        foreach (var name in html)
        {
            var isRoot = string.Equals(name, RootInterface, StringComparison.Ordinal);
            var tags = tagsByInterface.TryGetValue(name, out var t) ? t : new List<JsonNode>();
            var source = isRoot ? Globals(root).ToList() : declared[name];
            var attributes = Attributes(name, source, partials);
            var hasChildren = html.Any(n => string.Equals(Str(Get(interfaces, n), "parent"), name, StringComparison.Ordinal));

            files.Add(new KeyValuePair<string, string>(name + ".g.cs", TypeFile(name, Get(interfaces, name), tags, hasChildren, source, attributes, partials)));
            if (isRoot)
            {
                files.Add(new KeyValuePair<string, string>("GlobalAttrs.g.cs", GlobalFields(attributes)));
            }
        }

        return files;
    }

    private static Dictionary<string, List<JsonNode>> TagsByInterface(JsonNode root)
    {
        var tagsByInterface = new Dictionary<string, List<JsonNode>>(StringComparer.Ordinal);
        foreach (var e in Get(root, "elements").Items.Where(e => string.Equals(Str(e, "namespace"), "html", StringComparison.Ordinal)))
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

    // Every HTML interface a tag uses, and the chain up to HTMLElement.
    private static SortedSet<string> HtmlInterfaces(JsonNode interfaces, IEnumerable<string> tagged)
    {
        var html = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in tagged)
        {
            for (var n = name; n is not null && !string.Equals(n, "Element", StringComparison.Ordinal); n = interfaces[n] is { } d ? Str(d, "parent") : null)
            {
                html.Add(n);
            }
        }

        return html;
    }

    // Each attribute is declared on the interface whose IDL declares it (`on`), once.
    private static Dictionary<string, List<JsonNode>> DeclaredAttributes(JsonNode interfaces, SortedSet<string> html)
    {
        var declared = html.ToDictionary(n => n, _ => new List<JsonNode>(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in html)
        {
            if (Get(interfaces, name)["attributes"] is not { } attrs)
            {
                continue;
            }

            foreach (var a in attrs.Items)
            {
                var on = Str(a, "on") ?? name;
                if (on is "HTMLElement" or "Element" or "Node" || !html.Contains(on))
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

    // The attributes generated onto one interface: all it declares, less what a hand-written partial owns.
    private static List<(string Attr, string Prop, string Type, string Write)> Attributes(string name, List<JsonNode> source, Partials partials)
    {
        var ownedHere = partials.Owned.TryGetValue(name, out var o) ? new HashSet<string>(o, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        var attributes = new List<(string Attr, string Prop, string Type, string Write)>();
        foreach (var a in source)
        {
            var attr = Str(a, "attr")!;
            var prop = PropertyName(name, a);
            if (ownedHere.Contains(prop) || Omitted.Contains(name + "." + attr))
            {
                continue;
            }

            if (ReservedMembers.Contains(prop))
            {
                throw new DomEmitException($"{name}.{attr} maps to '{prop}', which Rask's Element already declares. Add an alias to DomEmitter.PropertyAliases.");
            }

            var type = CSharpType(Str(a, "type"));
            attributes.Add((attr, prop, type, WriteCall(name, attr, prop, type, a)));
            ownedHere.Add(prop);
        }

        return attributes;
    }

    private static string TypeFile(
        string name, JsonNode iface, List<JsonNode> tags, bool hasChildren, List<JsonNode> source,
        List<(string Attr, string Prop, string Type, string Write)> attributes, Partials partials)
    {
        var isRoot = string.Equals(name, RootInterface, StringComparison.Ordinal);
        var typed = partials.Typed.TryGetValue(name, out var typeParameter);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Text;");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core.Components;");
        sb.AppendLine();
        Doc(sb, "", TypeSummary(name, tags), iface);
        if (!typed)
        {
            Tags(sb, tags);
        }

        sb.Append("public ").Append(Modifier(isRoot, tags.Count == 0 || typed, hasChildren)).Append("partial class ").Append(name).Append(" : ")
            .AppendLine(isRoot ? "global::Rask.Core.Element" : Str(iface, "parent"));
        sb.AppendLine("{");
        Properties(sb, isRoot, attributes, source);
        if (attributes.Count > 0 || partials.HandWritten.Contains(name))
        {
            WriteAttributesOverride(sb, isRoot, attributes);
        }

        sb.AppendLine("}");
        if (typed)
        {
            // The tags go on the typed control, which is what an entry builds.
            sb.AppendLine();
            Tags(sb, tags);
            sb.Append("public sealed partial class ").Append(name).Append('<').Append(typeParameter).AppendLine(">;");
        }

        return sb.ToString();
    }

    private static string Modifier(bool isRoot, bool isAbstract, bool hasChildren)
    {
        if (isRoot)
        {
            return "";
        }

        if (isAbstract)
        {
            return "abstract ";
        }

        return hasChildren ? "" : "sealed ";
    }

    private static void Properties(StringBuilder sb, bool isRoot, List<(string Attr, string Prop, string Type, string Write)> attributes, List<JsonNode> source)
    {
        foreach (var (attr, prop, type, _) in attributes)
        {
            var a = source.First(x => string.Equals(Str(x, "attr"), attr, StringComparison.Ordinal));
            Doc(sb, "    ", AttributeSummary(attr, a), a);
            if (isRoot)
            {
                // A global lives on the lazily allocated side object: HTMLElement is the base of every
                // element, and a field each would cost every node of every live page. Assigning null
                // to an element that never set one allocates nothing.
                sb.Append("    public ").Append(type).Append(' ').Append(prop).AppendLine();
                sb.AppendLine("    {");
                sb.Append("        get => GlobalAttrsInternal?.").Append(prop).AppendLine(";");
                sb.Append("        set { if (value is not null || GlobalAttrsInternal is not null) GlobalAttrsForWrite.").Append(prop).AppendLine(" = value; }");
                sb.AppendLine("    }");
            }
            else
            {
                sb.Append("    public ").Append(type).Append(' ').Append(prop).AppendLine(" { get; set; }");
            }

            sb.AppendLine();
        }
    }

    private static void WriteAttributesOverride(StringBuilder sb, bool isRoot, List<(string Attr, string Prop, string Type, string Write)> attributes)
    {
        sb.AppendLine("    protected override void WriteAttributes(StringBuilder sb)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.WriteAttributes(sb);");
        sb.AppendLine("        WriteOwnedAttributesFirst(sb);");
        if (isRoot)
        {
            // One null check for the common element, which names no global at all.
            sb.AppendLine("        if (GlobalAttrsInternal is not null)");
            sb.AppendLine("        {");
        }

        foreach (var (_, _, _, write) in attributes)
        {
            sb.Append(isRoot ? "            " : "        ").AppendLine(write);
        }

        if (isRoot)
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

    // The side object's fields for the generated globals (see Component.GlobalAttrs).
    private static string GlobalFields(List<(string Attr, string Prop, string Type, string Write)> globals)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> from src/Rask.Core/Dom/mdn.snapshot.json by Rask.Dom.targets — do not edit.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("public abstract partial class Component");
        sb.AppendLine("{");
        sb.AppendLine("    internal sealed partial class GlobalAttrs");
        sb.AppendLine("    {");
        foreach (var (_, prop, type, _) in globals)
        {
            sb.Append("        public ").Append(type).Append(' ').Append(prop).AppendLine(";");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Tags(StringBuilder sb, List<JsonNode> tags)
    {
        foreach (var tag in tags)
        {
            var tagName = Str(tag, "tag")!;
            sb.Append("[global::Rask.Core.Tag(\"").Append(tagName).Append('"');
            if (EntryAliases.TryGetValue(tagName, out var entry))
            {
                sb.Append(", Entry = \"").Append(entry).Append('"');
            }

            sb.AppendLine(")]");
        }
    }

    private static IEnumerable<JsonNode> Globals(JsonNode root) =>
        Get(Get(root, "globalAttributes"), "html").Items
            .Where(a => !ElementOwnedGlobals.Contains(Str(a, "attr")!) && Str(a, "property") is not null);

    private static string PropertyName(string iface, JsonNode a)
    {
        var attr = Str(a, "attr")!;
        if (PropertyAliases.TryGetValue(iface + "." + attr, out var scoped))
        {
            return scoped;
        }

        var idl = Str(a, "property");
        if (idl is null)
        {
            return string.Concat(attr.Split('-').Select(Pascal));
        }

        if (PropertyAliases.TryGetValue(idl, out var alias))
        {
            return alias;
        }

        // An IDL attribute that holds an element (`commandForElement`) is written from markup as the id it
        // points at, so the property is named for the attribute it sets: CommandFor.
        if (!IsPlain(Str(a, "type")) && idl.EndsWith("Element", StringComparison.Ordinal) && idl.Length > "Element".Length)
        {
            idl = idl.Substring(0, idl.Length - "Element".Length);
        }

        return Pascal(idl);
    }

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

    private static string WriteCall(string iface, string attr, string prop, string type, JsonNode a)
    {
        var name = Literal(attr);
        switch (type)
        {
            case "bool?":
                return KeywordBooleans.TryGetValue(attr, out var kw)
                    ? $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)} ? {Literal(kw.On)} : {Literal(kw.Off)});"
                    : $"if ({prop} is true) AppendAttr(sb, {name}, null);";
            case "int?":
                return $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)});";
            case "double?":
                return $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)}.ToString(global::System.Globalization.CultureInfo.InvariantCulture));";
        }

        if (a["url"]?.AsBoolean() == true && !UrlLists.Contains(attr))
        {
            var helper = MediaUrlInterfaces.Contains(iface) && attr is "src" or "poster" ? "AppendMediaUrlAttr" : "AppendUrlAttr";
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
    private static void Doc(StringBuilder sb, string indent, string summary, JsonNode data)
    {
        sb.Append(indent).Append("/// <summary>").Append(summary).AppendLine("</summary>");
        var remarks = new List<string>();
        if (data["experimental"]?.AsBoolean() == true)
        {
            remarks.Add("<b>Experimental.</b>");
        }

        if (data["support"] is { Kind: JsonKind.Object } support)
        {
            var line = string.Join(" · ", support.Members.Select(p => $"{Browser(p.Key)} {p.Value.AsString()}"));
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

    private static string Browser(string id) => id switch
    {
        "chrome" => "Chrome",
        "chrome_android" => "Chrome Android",
        "firefox" => "Firefox",
        "firefox_android" => "Firefox Android",
        "safari" => "Safari",
        "safari_ios" => "Safari iOS",
        _ => id,
    };

    private static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static string Local(string prop) => "@" + char.ToLowerInvariant(prop[0]) + prop.Substring(1);

    private static string Literal(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string? Str(JsonNode e, string name) => e[name]?.AsString();

    private static JsonNode Get(JsonNode e, string name) =>
        e[name] ?? throw new DomEmitException($"the snapshot has no `{name}` where one was expected");

    internal static JsonNode Parse(string json)
    {
        var result = JsonLite.Parse(json);
        return result.Root ?? throw new DomEmitException($"not JSON ({result.Defect} at {result.Line}:{result.Column})");
    }
}
