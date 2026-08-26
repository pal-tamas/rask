using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Translations;

/// <summary>
///     Turns <c>Resources/{Family}.{culture}.json</c> into compile-checked members:
///     <c>Strings.Greeting(user.Name)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The point is that a missing key or a wrong argument count is a <b>C# compile error</b> —
///         CS0117 and CS1501 — rather than a blank on a page or a bare key shown to a user, which is
///         what a stringly-typed lookup gives you. Nothing here reflects, and nothing ships in a
///         satellite assembly, so a fully trimmed WASM publish keeps every string.
///     </para>
///     <para>
///         Lookup is keyed on a plain BCP 47 <b>string</b>, never a <c>CultureInfo</c>. That is what lets
///         translated text work under <c>InvariantGlobalization</c>, where constructing a culture throws:
///         an app can ship in three languages with no ICU at all, and only placeholder <em>formatting</em>
///         falls back to invariant.
///     </para>
/// </remarks>
[Generator]
public sealed class TranslationCatalogGenerator : IIncrementalGenerator
{
    private const string DefaultNeutral = "en";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            .Select(static (f, ct) => (Path: f.Path, Text: f.GetText(ct)?.ToString() ?? string.Empty))
            .Collect();

        var options = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
        {
            p.GlobalOptions.TryGetValue("build_property.RaskNeutralLanguage", out var neutral);
            p.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns);
            return (Neutral: string.IsNullOrWhiteSpace(neutral) ? DefaultNeutral : neutral!.Trim(),
                Namespace: string.IsNullOrWhiteSpace(ns) ? "Rask.Generated" : ns!.Trim());
        });

        context.RegisterSourceOutput(files.Combine(options), static (spc, pair) =>
            Emit(spc, pair.Left, pair.Right.Neutral, pair.Right.Namespace));
    }

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<(string Path, string Text)> files,
        string neutral,
        string rootNamespace)
    {
        var families = new Dictionary<string, List<Catalog>>(System.StringComparer.Ordinal);

        foreach (var (path, text) in files)
        {
            if (!TryDescribe(path, out var family, out var tag))
            {
                // Not named like a catalog, so it is just an app's own JSON sitting in the same folder.
                // Silence is correct here: an "orphan file" diagnostic on every data file would be noise.
                continue;
            }

            var catalog = new Catalog(path, family, tag);
            JsonCatalogReader.Read(text, catalog);

            foreach (var defect in catalog.Defects)
            {
                Report(spc, TranslationDiagnostics.Malformed, path, defect.Line, defect.Column,
                    System.IO.Path.GetFileName(path), defect.Reason);
            }

            if (catalog.Defects.Count > 0)
            {
                continue;
            }

            if (!families.TryGetValue(family, out var list))
            {
                list = [];
                families[family] = list;
            }

            list.Add(catalog);
        }

        foreach (var family in families.Keys.OrderBy(static k => k, System.StringComparer.Ordinal))
        {
            EmitFamily(spc, family, families[family], neutral, rootNamespace);
        }
    }

    private static void EmitFamily(
        SourceProductionContext spc,
        string family,
        List<Catalog> catalogs,
        string neutral,
        string rootNamespace)
    {
        var neutralCatalog = catalogs.FirstOrDefault(c =>
            string.Equals(c.CultureTag, neutral, System.StringComparison.OrdinalIgnoreCase));

        if (neutralCatalog is null)
        {
            // Without a neutral catalog there is no answer to "which keys exist", so nothing can be
            // generated and every call site would fail to compile with no useful explanation.
            Report(spc, TranslationDiagnostics.Malformed, catalogs[0].FilePath, 1, 1,
                family,
                $"there is no catalog for the neutral language '{neutral}' — add Resources/{family}.{neutral}.json, "
                + "or set <RaskNeutralLanguage> to a language this app does ship");
            return;
        }

        // Sorted so the generated switch, and therefore the compiled output, is stable across builds.
        var ordered = catalogs
            .OrderBy(c => c.CultureTag, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parsed = new Dictionary<string, ParsedMessage>(System.StringComparer.Ordinal);
        foreach (var key in neutralCatalog.Order)
        {
            var message = MessageParser.Parse(neutralCatalog.Entries[key].Value);
            if (message.Error is not null)
            {
                Report(spc, TranslationDiagnostics.Malformed, neutralCatalog.FilePath,
                    neutralCatalog.Entries[key].Line, neutralCatalog.Entries[key].Column,
                    System.IO.Path.GetFileName(neutralCatalog.FilePath),
                    $"the text for '{key}' has {message.Error}");
                return;
            }

            parsed[key] = message;
        }

        // Cross-check every translation against the neutral catalog before emitting anything.
        foreach (var catalog in ordered)
        {
            if (ReferenceEquals(catalog, neutralCatalog))
            {
                continue;
            }

            var neutralName = System.IO.Path.GetFileName(neutralCatalog.FilePath);
            var name = System.IO.Path.GetFileName(catalog.FilePath);

            foreach (var key in neutralCatalog.Order)
            {
                if (!catalog.Entries.ContainsKey(key))
                {
                    Report(spc, TranslationDiagnostics.Disagrees, catalog.FilePath, 1, 1,
                        name, neutralName,
                        $"no translation for '{key}' — the '{neutral}' text is used at runtime");
                }
            }

            foreach (var key in catalog.Order)
            {
                if (!neutralCatalog.Entries.ContainsKey(key))
                {
                    Report(spc, TranslationDiagnostics.Disagrees, catalog.FilePath,
                        catalog.Entries[key].Line, catalog.Entries[key].Column, name, neutralName,
                        $"'{key}' is not in the neutral catalog, so it generates nothing — add it to "
                        + $"'{neutralName}', or delete it here");
                    continue;
                }

                var message = MessageParser.Parse(catalog.Entries[key].Value);
                if (message.Error is not null)
                {
                    Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath,
                        catalog.Entries[key].Line, catalog.Entries[key].Column, name,
                        $"the text for '{key}' has {message.Error}");
                    return;
                }

                // A placeholder set that disagrees is not a style problem: string.Format throws
                // FormatException the first time that string is rendered, in that language only.
                var expected = parsed[key].Placeholders.Select(static p => p.Name).OrderBy(static n => n,
                    System.StringComparer.Ordinal);
                var actual = message.Placeholders.Select(static p => p.Name).OrderBy(static n => n,
                    System.StringComparer.Ordinal);
                if (!expected.SequenceEqual(actual, System.StringComparer.Ordinal))
                {
                    Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath,
                        catalog.Entries[key].Line, catalog.Entries[key].Column, name,
                        $"'{key}' uses placeholders {{{string.Join(", ", message.Placeholders.Select(static p => p.Name))}}} "
                        + $"but the neutral catalog uses {{{string.Join(", ", parsed[key].Placeholders.Select(static p => p.Name))}}} "
                        + "— a mismatched set throws FormatException at runtime");
                    return;
                }
            }
        }

        spc.AddSource($"{family}.g.cs", SourceText.From(
            Render(family, ordered, neutralCatalog, parsed, rootNamespace), Encoding.UTF8));
    }

    private static string Render(
        string family,
        List<Catalog> catalogs,
        Catalog neutralCatalog,
        Dictionary<string, ParsedMessage> parsed,
        string rootNamespace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"///     Translated text from Resources/{family}.*.json. Generated — edit the JSON, not this file.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static partial class {family}");
        sb.AppendLine("{");

        var tree = BuildTree(neutralCatalog.Order);
        RenderNode(sb, tree, catalogs, neutralCatalog, parsed, indent: 1);

        RenderCatalogPlumbing(sb, catalogs, neutralCatalog);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new(System.StringComparer.Ordinal);
        public List<string> Order { get; } = [];
        public string? Key { get; set; }
    }

    private static Node BuildTree(List<string> keys)
    {
        var root = new Node();
        foreach (var key in keys)
        {
            var node = root;
            var parts = key.Split('.');
            for (var i = 0; i < parts.Length; i++)
            {
                if (!node.Children.TryGetValue(parts[i], out var child))
                {
                    child = new Node();
                    node.Children[parts[i]] = child;
                    node.Order.Add(parts[i]);
                }

                node = child;
                if (i == parts.Length - 1)
                {
                    node.Key = key;
                }
            }
        }

        return root;
    }

    private static void RenderNode(
        StringBuilder sb,
        Node node,
        List<Catalog> catalogs,
        Catalog neutralCatalog,
        Dictionary<string, ParsedMessage> parsed,
        int indent)
    {
        var pad = new string(' ', indent * 4);

        foreach (var name in node.Order)
        {
            var child = node.Children[name];

            if (child.Key is { } key)
            {
                RenderMember(sb, pad, name, key, catalogs, neutralCatalog, parsed);
                continue;
            }

            sb.AppendLine($"{pad}/// <summary>Translated text under \"{name}\".</summary>");
            sb.AppendLine($"{pad}public static class {name}");
            sb.AppendLine($"{pad}{{");
            RenderNode(sb, child, catalogs, neutralCatalog, parsed, indent + 1);
            sb.AppendLine($"{pad}}}");
        }
    }

    private static void RenderMember(
        StringBuilder sb,
        string pad,
        string name,
        string key,
        List<Catalog> catalogs,
        Catalog neutralCatalog,
        Dictionary<string, ParsedMessage> parsed)
    {
        var message = parsed[key];
        var member = Escape(name);

        sb.AppendLine($"{pad}/// <summary><c>{neutralCatalog.CultureTag}</c>: {Xml(neutralCatalog.Entries[key].Value)}</summary>");

        if (message.Placeholders.Count == 0)
        {
            // A constant string per culture: the switch returns an interned literal, so reading one of
            // these allocates nothing at all.
            sb.AppendLine($"{pad}public static string {member} => __Index switch");
            sb.AppendLine($"{pad}{{");
            AppendArms(sb, pad + "    ", catalogs, neutralCatalog, key, static (m) => Literal(m.Format));
            sb.AppendLine($"{pad}}};");
            sb.AppendLine();
            return;
        }

        var parameters = string.Join(", ", message.Placeholders.Select(p => $"{p.ClrType} {Escape(p.Name)}"));
        var arguments = string.Join(", ", message.Placeholders.Select(p => Escape(p.Name)));

        sb.AppendLine($"{pad}public static string {member}({parameters})");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    var __format = __Index switch");
        sb.AppendLine($"{pad}    {{");
        AppendArms(sb, pad + "        ", catalogs, neutralCatalog, key, static (m) => Literal(m.Format));
        sb.AppendLine($"{pad}    }};");
        sb.AppendLine($"{pad}    return string.Format(");
        sb.AppendLine($"{pad}        global::Rask.Core.Globalization.RaskCulture.Current, __format, {arguments});");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine();
    }

    private static void AppendArms(
        StringBuilder sb,
        string pad,
        List<Catalog> catalogs,
        Catalog neutralCatalog,
        string key,
        System.Func<ParsedMessage, string> render)
    {
        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];
            if (ReferenceEquals(catalog, neutralCatalog) || !catalog.Entries.TryGetValue(key, out var entry))
            {
                continue;
            }

            var message = MessageParser.Parse(entry.Value);
            sb.AppendLine($"{pad}{i} => {render(message)},");
        }

        // The neutral text is the default arm, so an untranslated key falls back to it without any
        // runtime lookup — and "the key itself" is a state that cannot occur.
        sb.AppendLine($"{pad}_ => {render(MessageParser.Parse(neutralCatalog.Entries[key].Value))},");
    }

    private static void RenderCatalogPlumbing(StringBuilder sb, List<Catalog> catalogs, Catalog neutralCatalog)
    {
        var neutralIndex = catalogs.IndexOf(neutralCatalog);

        sb.AppendLine("    // Which catalog the active UI language resolves to. A switch over a plain BCP 47");
        sb.AppendLine("    // string, never a CultureInfo: constructing one throws under InvariantGlobalization,");
        sb.AppendLine("    // so keying on the string is what lets translated text work with no ICU at all.");
        sb.AppendLine("    private static int __Index");
        sb.AppendLine("    {");
        sb.AppendLine("        get");
        sb.AppendLine("        {");
        sb.AppendLine("            var __tag = global::Rask.Core.Globalization.RaskCulture.CurrentUI.Name;");
        sb.AppendLine("            while (__tag.Length > 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                switch (__tag)");
        sb.AppendLine("                {");

        for (var i = 0; i < catalogs.Count; i++)
        {
            sb.AppendLine($"                    case \"{catalogs[i].CultureTag}\": return {i};");
        }

        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                // hu-HU -> hu -> the neutral catalog. Sliced rather than Substring'd:");
        sb.AppendLine("                // this runs on the render path, and an allocation per lookup would show up.");
        sb.AppendLine("                var __cut = __tag.LastIndexOf('-');");
        sb.AppendLine("                if (__cut <= 0) break;");
        sb.AppendLine("                __tag = __tag.Substring(0, __cut);");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            return {neutralIndex};");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    // Resources/Strings.hu-HU.json -> family "Strings", tag "hu-HU".
    private static bool TryDescribe(string path, out string family, out string tag)
    {
        family = string.Empty;
        tag = string.Empty;

        var dir = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        if (dir.IndexOf("Resources", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return false;
        }

        family = name.Substring(0, dot);
        tag = name.Substring(dot + 1);
        return IsIdentifier(family) && IsCultureTag(tag);
    }

    private static bool IsCultureTag(string tag)
    {
        if (tag.Length is < 2 or > 32)
        {
            return false;
        }

        var subtag = 0;
        foreach (var part in tag.Split('-'))
        {
            if (part.Length is 0 or > 8)
            {
                return false;
            }

            foreach (var c in part)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    return false;
                }
            }

            if (subtag == 0)
            {
                if (part.Length < 2)
                {
                    return false;
                }

                foreach (var c in part)
                {
                    if (!char.IsLetter(c))
                    {
                        return false;
                    }
                }
            }

            subtag++;
        }

        return subtag > 0;
    }

    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0 || (!char.IsLetter(s[0]) && s[0] != '_'))
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string Escape(string name) => IsKeyword(name) ? "@" + name : name;

    private static bool IsKeyword(string name) => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name)
        != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;

    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(value).ToFullString();

    private static string Xml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void Report(
        SourceProductionContext spc,
        DiagnosticDescriptor descriptor,
        string path,
        int line,
        int column,
        params object[] args)
    {
        var position = new LinePosition(System.Math.Max(0, line - 1), System.Math.Max(0, column - 1));
        var location = Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(position, position));
        spc.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
    }
}
