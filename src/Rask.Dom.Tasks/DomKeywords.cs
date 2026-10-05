using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Generators.Json;

namespace Rask.Core.Dom.Build;

// HTML's enumerated attributes as C# enums, from the keywords the snapshot records for them (scripts/mdn/refresh.mjs):
// `Img.Loading(Loading.Lazy)` where it was `Img.Loading("lazy")`. An attribute is typed when its set is closed and every
// keyword names a member mechanically ("plaintext-only" → PlaintextOnly). It stays a string where the spec's values are
// not words (`<ol type="A">`, a MIME type), and where they are a token list (`rel`): a set of keywords, not one.
//
// A type is named after its step (`Loading`), after its tag where only one tag carries the attribute (`TrackKind`) or
// where tags carrying it disagree on its keywords (`ButtonType`), and after the IDL enum the attribute reflects where
// there is one (`ReferrerPolicy`). Each value's number is its keyword's FNV-1a hash (DomValueTypes.Hashes).
internal sealed class DomKeywords
{
    // On every enum whose members are values an attribute takes: the chain generator offers no step per member, since
    // `Img.Lazy` would read as the image being lazy rather than as its loading.
    public const string Marker = "[global::System.CodeDom.Compiler.GeneratedCode(\"Rask.Dom.Keywords\", \"1.0\")]";

    // A keyword that names a member as it is: lower-case words joined by hyphens.
    private static readonly Regex Word = new("^[a-z]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private readonly Dictionary<string, KeywordType> _byAttribute = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, KeywordType> _types = new(StringComparer.Ordinal);

    private DomKeywords()
    {
    }

    // The keyword types of a snapshot. `types` declares the IDL enums among them; `partials` may hold a hand-written enum
    // in a generated one's place (InputType), which must have exactly the members the spec's keywords name.
    public static DomKeywords Read(JsonNode root, Partials partials, DomValueTypes types)
    {
        var result = new DomKeywords();
        var candidates = Candidates(root).ToList();
        var tagCounts = TagCounts(root);
        foreach (var byAttr in candidates.GroupBy(c => c.Attr, StringComparer.Ordinal))
        {
            var sets = byAttr.GroupBy(c => c.Signature, StringComparer.Ordinal).ToList();
            foreach (var set in sets)
            {
                var first = set.First();
                var name = first.Enum ?? first.Step;
                if (first.Enum is null && !first.Global && (sets.Count > 1 || tagCounts.TryGetValue(first.Attr, out var count) && count == 1))
                {
                    var tags = set.SelectMany(c => c.Tags).Distinct(StringComparer.Ordinal).ToList();
                    name = tags.Count == 1
                        ? DomRefEmitter.Pascal(tags[0]) + first.Step
                        : throw new DomEmitException($"the `{first.Attr}` keywords of {string.Join(", ", tags)} differ from another tag's, and name no single tag to prefix.");
                }

                result.Add(name, set.ToList(), root, partials, types);
            }
        }

        return result;
    }

    // Every keyword type's name: what Rask.Web may not declare a root type of (both namespaces are global usings).
    public IEnumerable<string> Names => _types.Keys;

    // The C# type of an attribute (an element's, or a global's with `global`), or null where it stays a string.
    public string? TypeOf(JsonNode attribute, bool global = false) =>
        _byAttribute.TryGetValue(Key(attribute, global), out var type) ? "global::Rask.Core." + type.Name : null;

    // `writingsuggestions="true|false"`: a boolean in all but its IDL type.
    public static bool IsTrueFalse(JsonNode attribute) =>
        KeywordsOf(attribute) is { Count: 2 } v && string.Equals(v[0], "true", StringComparison.Ordinal) && string.Equals(v[1], "false", StringComparison.Ordinal);

    // A boolean attribute whose spec names its two states (`autocorrect="on|off"`): the first for true, the second for false.
    public static (string On, string Off)? BooleanKeywords(JsonNode attribute) =>
        KeywordsOf(attribute) is { Count: 2 } v ? (v[0], v[1]) : null;

    // The enums (less the IDL ones DomValueTypes declares, and the hand-written ones), and each type's keyword text, which
    // the render writes from a switch of literals: no allocation, and no reflection for a trimmed app.
    public string File()
    {
        var sb = new StringBuilder();
        DomValueTypes.Header(sb);
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        foreach (var type in _types.Values.Where(t => t.Source == Source.Spec))
        {
            WriteEnum(sb, type);
        }

        sb.AppendLine("// The text each keyword renders as.");
        sb.AppendLine("internal static class KeywordText");
        sb.AppendLine("{");
        foreach (var type in _types.Values.Where(t => t.Source != Source.HandWritten))
        {
            var qualified = "global::Rask.Core." + type.Name;
            sb.Append("    internal static string Of(").Append(qualified).AppendLine(" value) => value switch");
            sb.AppendLine("    {");
            foreach (var value in type.Values)
            {
                sb.Append("        ").Append(qualified).Append('.').Append(DomValueTypes.EnumMember(value)).Append(" => \"").Append(value).AppendLine("\",");
            }

            sb.AppendLine("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(value), value, null),");
            sb.AppendLine("    };");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private enum Source
    {
        Spec,
        Idl,
        HandWritten,
    }

    private sealed class KeywordType(string name, List<string> values, List<JsonNode> keywords, Source source, string attr, List<string> tags)
    {
        public string Name { get; } = name;

        public List<string> Values { get; } = values;

        public List<JsonNode> Keywords { get; } = keywords;

        public Source Source { get; } = source;

        public string Attr { get; } = attr;

        public List<string> Tags { get; } = tags;
    }

    // One attribute that may be typed: where it is (a global, or an interface's with the tags that carry it), its step's
    // name, and its keywords.
    private sealed class Candidate(string attr, bool global, string step, string? idlEnum, List<string> tags, JsonNode node)
    {
        public string Attr { get; } = attr;

        public bool Global { get; } = global;

        public string Step { get; } = step;

        public string? Enum { get; } = idlEnum;

        public List<string> Tags { get; } = tags;

        public JsonNode Node { get; } = node;

        public string Signature => string.Join(" ", KeywordsOf(Node)!);
    }

    private void Add(string name, List<Candidate> set, JsonNode root, Partials partials, DomValueTypes types)
    {
        var first = set[0];
        var values = KeywordsOf(first.Node)!;
        var members = values.Select(DomValueTypes.EnumMember).ToList();
        var duplicate = members.GroupBy(m => m, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomEmitException($"{name}'s keywords name the member {duplicate.Key} twice.");
        }

        var source = Source.Spec;
        if (partials.Enums.TryGetValue(name, out var handWritten))
        {
            if (!handWritten.SetEquals(members))
            {
                throw new DomEmitException(
                    $"the hand-written {name} has members {string.Join(", ", handWritten.OrderBy(m => m, StringComparer.Ordinal))}, "
                    + $"where the spec's `{first.Attr}` keywords name {string.Join(", ", members.OrderBy(m => m, StringComparer.Ordinal))}.");
            }

            source = Source.HandWritten;
        }
        else if (root["enums"]?[name] is { } idl)
        {
            if (!idl.Items.Select(v => v.AsString()).SequenceEqual(values, StringComparer.Ordinal))
            {
                throw new DomEmitException($"the `{first.Attr}` keywords would be named {name}, an IDL enum with other values.");
            }

            types.UseEnum(name);
            source = Source.Idl;
        }

        if (_types.TryGetValue(name, out var known))
        {
            if (!known.Values.SequenceEqual(values, StringComparer.Ordinal))
            {
                throw new DomEmitException($"the `{known.Attr}` and `{first.Attr}` keywords are both named {name}, with other values.");
            }
        }
        else
        {
            _types[name] = new KeywordType(name, values, first.Node["keywords"]!.Items.ToList(), source, first.Attr,
                set.SelectMany(c => c.Tags).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList());
        }

        foreach (var c in set)
        {
            _byAttribute[Key(c.Node, c.Global)] = _types[name];
        }
    }

    // The attributes the snapshot records closed keyword sets for that C# can type: a string the IDL reflects (not a
    // boolean, not a token list), with keywords that are words. An IDL enum's own values are names already.
    private static IEnumerable<Candidate> Candidates(JsonNode root)
    {
        var tagsByInterface = (root["elements"]?.Items ?? new List<JsonNode>())
            .Where(e => string.Equals(e["namespace"]?.AsString(), "html", StringComparison.Ordinal))
            .GroupBy(e => e["interface"]!.AsString()!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e["tag"]!.AsString()!).ToList(), StringComparer.Ordinal);
        var interfaces = root["interfaces"]!;
        foreach (var iface in interfaces.Members.Where(i => tagsByInterface.ContainsKey(i.Key)))
        {
            foreach (var a in (iface.Value["attributes"]?.Items ?? new List<JsonNode>()).Where(a => Typable(a) && !TokenList(interfaces, iface.Key, a) && !DomEmitter.Omitted.Contains(iface.Key + "." + a["attr"]!.AsString())))
            {
                var tags = a["tags"]?.Items.Select(t => t.AsString()!).ToList() ?? tagsByInterface[iface.Key];
                yield return new Candidate(a["attr"]!.AsString()!, false, StepOf(a), a["enum"]?.AsString(), tags, a);
            }
        }

        foreach (var a in (root["globalAttributes"]?["html"]?.Items ?? new List<JsonNode>()).Where(Typable))
        {
            yield return new Candidate(a["attr"]!.AsString()!, true, StepOf(a), a["enum"]?.AsString(), new List<string>(), a);
        }
    }

    private static bool Typable(JsonNode a) =>
        a["type"]?.AsString() is "DOMString" or "DOMString?"
        && KeywordsOf(a) is { Count: > 0 } values
        && !IsTrueFalse(a)
        && (a["enum"] is not null || values.All(Word.IsMatch));

    // `rel`, which its IDL also reflects as a DOMTokenList (`relList`): several keywords at once, not one of them.
    private static bool TokenList(JsonNode interfaces, string iface, JsonNode a)
    {
        var list = a["property"]?.AsString() + "List";
        for (var n = iface; n is not null && interfaces[n] is { } i; n = i["parent"]?.AsString())
        {
            if ((i["members"]?.Items ?? new List<JsonNode>()).Any(m =>
                    string.Equals(m["name"]?.AsString(), list, StringComparison.Ordinal) && string.Equals(m["type"]?.AsString(), "DOMTokenList", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    // How many tags, in either namespace, carry an attribute of each name.
    private static Dictionary<string, int> TagCounts(JsonNode root)
    {
        var tagsByInterface = (root["elements"]?.Items ?? new List<JsonNode>())
            .GroupBy(e => e["interface"]!.AsString()!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e["namespace"]!.AsString() + ":" + e["tag"]!.AsString()).ToList(), StringComparer.Ordinal);
        var carriers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var iface in root["interfaces"]!.Members.Where(i => tagsByInterface.ContainsKey(i.Key)))
        {
            var ns = iface.Value["namespace"]?.AsString();
            foreach (var a in iface.Value["attributes"]?.Items ?? new List<JsonNode>())
            {
                var attr = a["attr"]!.AsString()!;
                if (!carriers.TryGetValue(attr, out var tags))
                {
                    carriers[attr] = tags = new HashSet<string>(StringComparer.Ordinal);
                }

                tags.UnionWith(a["tags"]?.Items.Select(t => ns + ":" + t.AsString()) ?? tagsByInterface[iface.Key]);
            }
        }

        return carriers.ToDictionary(c => c.Key, c => c.Value.Count, StringComparer.Ordinal);
    }

    // The step the attribute is set with: its IDL attribute's name (`crossOrigin` → CrossOrigin).
    private static string StepOf(JsonNode a) =>
        a["property"]?.AsString() is { } property ? DomRefEmitter.Pascal(property) : string.Concat(a["attr"]!.AsString()!.Split('-').Select(DomRefEmitter.Pascal));

    private static List<string>? KeywordsOf(JsonNode attribute) =>
        attribute["keywords"]?.Items.Select(k => k["value"]!.AsString()!).ToList();

    // One attribute node's identity: a global by name, an element's by its place in the snapshot (the same node object
    // the emitter later asks about).
    private static string Key(JsonNode attribute, bool global) =>
        (global ? "global:" : "element:") + attribute["attr"]!.AsString() + ":" + string.Join(" ", KeywordsOf(attribute) ?? new List<string>());

    private static void WriteEnum(StringBuilder sb, KeywordType type)
    {
        var tags = type.Tags.Count == 0 ? "" : " (on " + string.Join(", ", type.Tags.Select(t => $"<c>&lt;{t}&gt;</c>")) + ")";
        sb.Append("/// <summary>The keywords of the <c>").Append(type.Attr).Append("</c> attribute").Append(tags).AppendLine(".</summary>");
        sb.AppendLine(Marker);
        sb.Append("public enum ").AppendLine(type.Name);
        sb.AppendLine("{");
        var hashes = DomValueTypes.Hashes(type.Name, type.Values);
        for (var i = 0; i < type.Values.Count; i++)
        {
            DomEmitter.Member(sb, "    ", $"The keyword <c>\"{type.Values[i]}\"</c>.", type.Keywords[i]);
            sb.Append("    ").Append(DomValueTypes.EnumMember(type.Values[i])).Append(" = ").Append(hashes[i]).AppendLine(",");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }
}
