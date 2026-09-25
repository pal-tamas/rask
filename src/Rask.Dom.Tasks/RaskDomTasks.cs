// Rask.Core's build-time MDN tasks, loaded by src/Rask.Core/Dom/Rask.Dom.targets. Two jobs:
//   RaskMdnRefresh  keeps mdn.snapshot.json on MDN's latest stable data (local builds, at most once a day)
//   RaskDomEmit     writes one partial per DOM interface into obj/, from the snapshot, before the compile
// The emitted code is ordinary source to the chain generator, which is why it is written here and not by a
// Roslyn generator: generators never see each other's output.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build
{
    public sealed class RaskDomEmit : Task
    {
        [Required] public string Snapshot { get; set; } = "";

        // The hand-written partials beside the generated types: a member one declares is not generated.
        public ITaskItem[] Partials { get; set; } = Array.Empty<ITaskItem>();

        [Required] public string OutputDirectory { get; set; } = "";

        [Output] public ITaskItem[] Generated { get; set; } = Array.Empty<ITaskItem>();

        public override bool Execute()
        {
            var partials = DomEmitter.ReadPartials(Partials.Select(p => File.ReadAllText(p.ItemSpec)));
            IReadOnlyList<KeyValuePair<string, string>> files;
            try
            {
                files = DomEmitter.Emit(File.ReadAllText(Snapshot), partials);
            }
            catch (DomEmitException e)
            {
                Log.LogError(null, "RASKDOM001", null, Snapshot, 0, 0, 0, 0, e.Message);
                return false;
            }

            Directory.CreateDirectory(OutputDirectory);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var path = Path.Combine(OutputDirectory, file.Key);
                written.Add(path);
                // Only-if-changed, so an unchanged snapshot never retriggers the compile.
                if (!File.Exists(path) || File.ReadAllText(path) != file.Value)
                {
                    File.WriteAllText(path, file.Value);
                }
            }

            foreach (var stale in Directory.GetFiles(OutputDirectory, "*.g.cs").Where(f => !written.Contains(f)))
            {
                File.Delete(stale);
            }

            Generated = written.OrderBy(p => p, StringComparer.Ordinal).Select(p => (ITaskItem)new TaskItem(p)).ToArray();
            return true;
        }
    }

    public sealed class RaskMdnRefresh : Task
    {
        [Required] public string Snapshot { get; set; } = "";

        [Required] public string RefreshScript { get; set; } = "";

        // ~/.rask/mdn: where the once-a-day stamp lives.
        [Required] public string StampDirectory { get; set; } = "";

        public override bool Execute()
        {
            // Every failure here is a message, never an error: an unreachable MDN falls back to the committed
            // snapshot, and the build goes on.
            try
            {
                Directory.CreateDirectory(StampDirectory);
                var stamp = Path.Combine(StampDirectory, "last-check");
                if (File.Exists(stamp) && DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < TimeSpan.FromDays(1))
                {
                    return true;
                }

                var current = DomEmitter.Sources(File.ReadAllText(Snapshot));
                var latest = MdnLatest.Resolve();
                File.WriteAllText(stamp, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                if (latest.All(kv => current.TryGetValue(kv.Key, out var v) && v == kv.Value))
                {
                    return true;
                }

                Log.LogMessage(MessageImportance.High, "MDN has newer data ({0}); refreshing the element snapshot…",
                    string.Join(", ", latest.Where(kv => !current.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => kv.Key + " " + kv.Value)));
                if (!Run(latest))
                {
                    return true;
                }

                Log.LogMessage(MessageImportance.High, "MDN refreshed: commit {0}", Snapshot);
            }
            catch (Exception e) when (e is IOException or HttpRequestException or AggregateException or System.Threading.Tasks.TaskCanceledException
                                          or FormatException or InvalidOperationException or KeyNotFoundException or System.ComponentModel.Win32Exception)
            {
                Log.LogMessage(MessageImportance.High, "MDN refresh skipped ({0}); building from the committed snapshot.", e.Message);
            }

            return true;
        }

        private bool Run(IReadOnlyDictionary<string, string> pins)
        {
            var info = new ProcessStartInfo("bash", "\"" + RefreshScript + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Environment["RASK_MDN_BCD"] = pins["@mdn/browser-compat-data"];
            info.Environment["RASK_MDN_IDL"] = pins["@webref/idl"];
            info.Environment["RASK_MDN_ELEMENTS"] = pins["@webref/elements"];
            info.Environment["RASK_MDN_WEBREF"] = pins["webref/dfns"];
            info.Environment["RASK_MDN_WEBIDL2"] = pins["webidl2"];
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(300_000))
            {
                process.Kill();
                Log.LogMessage(MessageImportance.High, "MDN refresh timed out; building from the committed snapshot.");
                return false;
            }

            if (process.ExitCode != 0)
            {
                Log.LogMessage(MessageImportance.High, "MDN refresh failed; building from the committed snapshot.\n{0}", error.Result);
                return false;
            }

            Log.LogMessage(MessageImportance.Normal, output.Result);
            return true;
        }
    }

    // The newest STABLE release of each source: npm's `latest` dist-tag (refused if a prerelease), and the head of
    // webref's curated branch, which is webref's validated channel.
    public static class MdnLatest
    {
        public static IReadOnlyDictionary<string, string> Resolve()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("rask-build");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in new[] { "@mdn/browser-compat-data", "@webref/idl", "@webref/elements", "webidl2" })
            {
                var tags = DomEmitter.Parse(http.GetStringAsync("https://registry.npmjs.org/-/package/" + package.Replace("/", "%2F") + "/dist-tags").Result);
                var latest = tags["latest"]?.AsString() ?? throw new FormatException("no `latest` dist-tag for " + package);
                // Stable only: a `latest` that points at a prerelease (8.2.0-beta.1) is not taken; the committed
                // snapshot stays until a stable release follows it.
                if (latest.IndexOf('-') >= 0 || latest.IndexOf('+') >= 0)
                {
                    throw new FormatException(package + "'s `latest` is the prerelease " + latest);
                }

                result[package] = latest;
            }

            var head = DomEmitter.Parse(http.GetStringAsync("https://api.github.com/repos/w3c/webref/commits/curated").Result);
            result["webref/dfns"] = head["sha"]?.AsString() ?? throw new FormatException("no sha for webref's curated branch");

            return result;
        }
    }

    public sealed class DomEmitException(string message) : Exception(message);

    // What the hand-written partials beside the generated types declare.
    public sealed class Partials
    {
        // Class -> the members it declares itself, which are therefore not generated.
        public Dictionary<string, HashSet<string>> Owned { get; } = new(StringComparer.Ordinal);

        // A typed control's MDN name -> its type parameter (HTMLInputElement -> T).
        public Dictionary<string, string> Typed { get; } = new(StringComparer.Ordinal);

        // Classes with a non-generic hand-written partial, which may implement WriteOwnedAttributes.
        public HashSet<string> HandWritten { get; } = new(StringComparer.Ordinal);
    }

    public static class DomEmitter
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

        public static IReadOnlyDictionary<string, string> Sources(string snapshotJson)
        {
            return Get(Parse(snapshotJson), "sources").Members.ToDictionary(p => p.Key, p => p.Value.AsString() ?? "", StringComparer.Ordinal);
        }

        // `class HTMLFormElement<[DynamicallyAccessedMembers(...)] TModel> : HTMLFormElement` included.
        private static readonly Regex TypedControl = new(@"class\s+(HTML\w*Element)\s*<(?:\s*\[[^\]]*\])?\s*(\w+)\s*>\s*:\s*\1\b", RegexOptions.Compiled);
        private static readonly Regex PartialClass = new(@"partial\s+class\s+(HTML\w*Element)\b(?!\s*<)", RegexOptions.Compiled);
        private static readonly Regex PublicProperty = new(@"^\s*public\s+(?:new\s+|override\s+|virtual\s+|required\s+)*[\w<>\[\]?.,: ]+?\s+(\w+)\s*(?:\{|=>)", RegexOptions.Compiled | RegexOptions.Multiline);

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
                    name = typed.Groups[1].Value;
                    result.Typed[name] = typed.Groups[2].Value;
                }
                else if (PartialClass.Match(source) is { Success: true } partial)
                {
                    name = partial.Groups[1].Value;
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
                    members.Add(m.Groups[1].Value);
                }
            }

            return result;
        }

        public static IReadOnlyList<KeyValuePair<string, string>> Emit(string snapshotJson, Partials partials)
        {
            var root = Parse(snapshotJson);
            var interfaces = Get(root, "interfaces");
            var tagsByInterface = new Dictionary<string, List<JsonNode>>(StringComparer.Ordinal);
            foreach (var e in Get(root, "elements").Items.Where(e => Str(e, "namespace") == "html"))
            {
                var i = Str(e, "interface")!;
                if (!tagsByInterface.TryGetValue(i, out var list))
                {
                    tagsByInterface[i] = list = new List<JsonNode>();
                }

                list.Add(e);
            }

            // Every HTML interface a tag uses, and the chain up to HTMLElement.
            var html = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var name in tagsByInterface.Keys)
            {
                for (var n = name; n is not null && n != "Element"; n = interfaces[n] is { } d ? Str(d, "parent") : null)
                {
                    html.Add(n);
                }
            }

            // Each attribute is declared on the interface whose IDL declares it (`on`), once.
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

            var files = new List<KeyValuePair<string, string>>();
            foreach (var name in html)
            {
                var iface = Get(interfaces, name);
                var parent = Str(iface, "parent");
                var tags = tagsByInterface.TryGetValue(name, out var t) ? t : new List<JsonNode>();
                var hasChildren = html.Any(n => Str(Get(interfaces, n), "parent") == name);
                var isRoot = name == "HTMLElement";
                var typed = partials.Typed.TryGetValue(name, out var typeParameter);
                var modifier = isRoot ? "" : tags.Count == 0 || typed ? "abstract " : hasChildren ? "" : "sealed ";
                var ownedHere = partials.Owned.TryGetValue(name, out var o) ? new HashSet<string>(o, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);

                var attributes = new List<(string Attr, string Prop, string Type, string Write)>();
                IEnumerable<JsonNode> source = isRoot ? Globals(root) : declared[name];
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

                sb.Append("public ").Append(modifier).Append("partial class ").Append(name).Append(" : ")
                    .AppendLine(isRoot ? "global::Rask.Core.Element" : parent);
                sb.AppendLine("{");
                foreach (var (attr, prop, type, _) in attributes)
                {
                    var a = source.First(x => Str(x, "attr") == attr);
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

                if (attributes.Count > 0 || partials.HandWritten.Contains(name))
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

                sb.AppendLine("}");
                if (typed)
                {
                    // The tags go on the typed control, which is what an entry builds.
                    sb.AppendLine();
                    Tags(sb, tags);
                    sb.Append("public sealed partial class ").Append(name).Append('<').Append(typeParameter).AppendLine(">;");
                }

                files.Add(new KeyValuePair<string, string>(name + ".g.cs", sb.ToString()));
                if (isRoot)
                {
                    files.Add(new KeyValuePair<string, string>("GlobalAttrs.g.cs", GlobalFields(attributes)));
                }
            }

            return files;
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
            if (type == "bool?")
            {
                return KeywordBooleans.TryGetValue(attr, out var kw)
                    ? $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)} ? {Literal(kw.On)} : {Literal(kw.Off)});"
                    : $"if ({prop} is true) AppendAttr(sb, {name}, null);";
            }

            if (type == "int?")
            {
                return $"if ({prop} is {{ }} {Local(prop)}) AppendAttr(sb, {name}, {Local(prop)});";
            }

            if (type == "double?")
            {
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
}
