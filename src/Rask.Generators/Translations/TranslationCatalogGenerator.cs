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

    // The catalog name an app uses to translate the framework's OWN text. Reserved because its keys are
    // not the app's to invent: they are RaskString members, and a name outside that set is a typo that
    // would otherwise translate nothing and say nothing.
    private const string ReservedFamily = "RaskStrings";

    // Mirrors Rask.Core.Globalization.RaskString. Duplicated rather than referenced because a Roslyn
    // analyzer cannot depend on the runtime assembly it generates code for. FrameworkStringsCatalogTests
    // asserts the two lists stay identical, so adding a RaskString member without updating this fails
    // there rather than silently making that string untranslatable.
    private static readonly string[] _frameworkStringKeys =
    [
        "PickerPreviousMonth",
        "PickerNextMonth",
        "PickerHour",
        "PickerMinute",
        "PickerSecond",
        "PickerClear",
        "NotFoundTitle",
        "NotFoundBody",
        "NotFoundBackHome",
        "ErrorHeading",
        "ErrorTryAgain",
        "ErrorReload",
    ];

    /// <summary>Test seam: the key list this generator accepts, mirrored from <c>RaskString</c>.</summary>
    internal static System.Collections.Generic.IReadOnlyList<string> FrameworkStringKeysForTests =>
        _frameworkStringKeys;

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
            if (string.Equals(family, ReservedFamily, System.StringComparison.Ordinal))
            {
                EmitFrameworkStrings(spc, families[family], rootNamespace);
                continue;
            }

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
            var entry = neutralCatalog.Entries[key];
            var neutralName = System.IO.Path.GetFileName(neutralCatalog.FilePath);

            if (entry.IsPlural)
            {
                if (!ValidatePlural(spc, neutralCatalog, entry, neutralName))
                {
                    return;
                }

                // Every form has to agree on placeholders too — they are the same call site.
                ParsedMessage? first = null;
                foreach (var form in entry.Forms.Values)
                {
                    var parsedForm = MessageParser.Parse(form);
                    if (parsedForm.Error is not null)
                    {
                        Report(spc, TranslationDiagnostics.Malformed, neutralCatalog.FilePath,
                            entry.Line, entry.Column, neutralName,
                            $"the text for '{key}' has {parsedForm.Error}");
                        return;
                    }

                    first ??= parsedForm;
                }

                parsed[key] = first ?? MessageParser.Parse(string.Empty);
                continue;
            }

            var message = MessageParser.Parse(entry.Value);
            if (message.Error is not null)
            {
                Report(spc, TranslationDiagnostics.Malformed, neutralCatalog.FilePath,
                    entry.Line, entry.Column, neutralName,
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

                if (neutralCatalog.Entries[key].IsPlural != catalog.Entries[key].IsPlural)
                {
                    Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath,
                        catalog.Entries[key].Line, catalog.Entries[key].Column, name,
                        $"'{key}' is {(catalog.Entries[key].IsPlural ? "a plural set here but a single string" : "a single string here but a plural set")} "
                        + "in the neutral catalog — they generate different members, so they cannot differ");
                    return;
                }

                if (catalog.Entries[key].IsPlural)
                {
                    if (!ValidatePlural(spc, catalog, catalog.Entries[key], name))
                    {
                        return;
                    }

                    // A category this language HAS but the catalog omits is a gap the reader will see
                    // as wrong grammar, so it is reported — but as a warning, like any missing text.
                    foreach (var category in PluralRules.CategoriesFor(catalog.CultureTag) ?? [])
                    {
                        if (!catalog.Entries[key].Forms.ContainsKey(category))
                        {
                            Report(spc, TranslationDiagnostics.Disagrees, catalog.FilePath,
                                catalog.Entries[key].Line, catalog.Entries[key].Column, name, neutralName,
                                $"'{key}' has no '{category}' form, which {catalog.CultureTag} distinguishes "
                                + "— the 'other' form is used instead");
                        }
                    }

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

    /// <summary>
    ///     Emits an <c>IRaskStringSource</c> for the app's translations of the framework's own text.
    /// </summary>
    /// <remarks>
    ///     No neutral catalog is required or wanted here: the framework's English lives as a literal at
    ///     each call site, which is what makes a missing framework string impossible. An app supplies
    ///     only the languages it has, and only the keys it has translated.
    /// </remarks>
    private static void EmitFrameworkStrings(
        SourceProductionContext spc, List<Catalog> catalogs, string rootNamespace)
    {
        var ordered = catalogs
            .OrderBy(c => c.CultureTag, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var catalog in ordered)
        {
            var name = System.IO.Path.GetFileName(catalog.FilePath);
            foreach (var key in catalog.Order)
            {
                if (catalog.Entries[key].IsPlural)
                {
                    Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath,
                        catalog.Entries[key].Line, catalog.Entries[key].Column, name,
                        $"'{key}' is a plural set, but the framework's own strings are plain text");
                    return;
                }

                if (System.Array.IndexOf(_frameworkStringKeys, key) < 0)
                {
                    Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath,
                        catalog.Entries[key].Line, catalog.Entries[key].Column, name,
                        $"'{key}' is not one of the framework's own strings — valid names are "
                        + string.Join(", ", _frameworkStringKeys));
                    return;
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>This app's translations of the text Rask itself renders.</summary>");
        sb.AppendLine("internal sealed class __RaskFrameworkStrings : global::Rask.Core.Globalization.IRaskStringSource");
        sb.AppendLine("{");
        sb.AppendLine("    public string? Get(global::Rask.Core.Globalization.RaskString key, string cultureTag)");
        sb.AppendLine("    {");
        sb.AppendLine("        // Walks hu-HU -> hu, then gives up so the caller uses the framework's English.");
        sb.AppendLine("        var tag = cultureTag;");
        sb.AppendLine("        while (tag.Length > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (tag)");
        sb.AppendLine("            {");

        foreach (var catalog in ordered)
        {
            sb.AppendLine($"                case \"{catalog.CultureTag}\":");
            sb.AppendLine("                    switch (key)");
            sb.AppendLine("                    {");
            foreach (var key in catalog.Order)
            {
                sb.AppendLine($"                        case global::Rask.Core.Globalization.RaskString.{key}:");
                sb.AppendLine($"                            return {Literal(catalog.Entries[key].Value)};");
            }

            sb.AppendLine("                    }");
            sb.AppendLine();
            sb.AppendLine("                    break;");
        }

        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            var cut = tag.LastIndexOf('-');");
        sb.AppendLine("            if (cut <= 0) break;");
        sb.AppendLine("            tag = tag.Substring(0, cut);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    // Registered without any AddRask() call, so translating the framework's text is a file an");
        sb.AppendLine("    // app drops in rather than wiring it up — and it works identically on Server and WASM.");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init() =>");
        sb.AppendLine("        global::Rask.Core.Globalization.RaskStrings.UseSource(new __RaskFrameworkStrings());");
        sb.AppendLine("}");

        spc.AddSource("RaskStrings.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
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
        RenderPluralFunctions(sb, catalogs, neutralCatalog);

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
                if (neutralCatalog.Entries[key].IsPlural)
                {
                    RenderPluralMember(sb, pad, name, key, catalogs, neutralCatalog);
                }
                else
                {
                    RenderMember(sb, pad, name, key, catalogs, neutralCatalog, parsed);
                }

                continue;
            }

            sb.AppendLine($"{pad}/// <summary>Translated text under \"{name}\".</summary>");
            sb.AppendLine($"{pad}public static class {name}");
            sb.AppendLine($"{pad}{{");
            RenderNode(sb, child, catalogs, neutralCatalog, parsed, indent + 1);
            sb.AppendLine($"{pad}}}");
        }
    }

    /// <summary>
    ///     Checks a plural entry against the grammar of the language it is written in.
    /// </summary>
    /// <remarks>
    ///     An unknown language is an error rather than a silent fallback to English rules. Applying the
    ///     wrong grammar produces text that reads as broken to every native speaker while every test
    ///     stays green — the failure mode a curated table exists to avoid.
    /// </remarks>
    private static bool ValidatePlural(
        SourceProductionContext spc, Catalog catalog, CatalogEntry entry, string name)
    {
        var categories = PluralRules.CategoriesFor(catalog.CultureTag);
        if (categories is null)
        {
            Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath, entry.Line, entry.Column, name,
                $"'{entry.Path}' is a plural set, but Rask does not carry the plural rules for "
                + $"'{catalog.CultureTag}' — open an issue to add them, or replace the plural key with "
                + "explicit per-count keys");
            return false;
        }

        if (entry.PluralParameter is not { Length: > 0 })
        {
            Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath, entry.Line, entry.Column, name,
                $"'{entry.Path}' has an empty '$plural' — name the parameter that carries the count");
            return false;
        }

        var residual = PluralRules.ResidualFor(catalog.CultureTag)!;
        if (!entry.Forms.ContainsKey(residual))
        {
            // The arm every unmatched count lands on. Note this is NOT always "other": Polish integers
            // never select "other" — CLDR routes the residual to "many" — so requiring "other" there
            // would demand dead text and leave the form the language really uses optional.
            Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath, entry.Line, entry.Column, name,
                $"'{entry.Path}' has no '{residual}' form, which is what {catalog.CultureTag} falls back to "
                + "for any count the other forms do not match");
            return false;
        }

        foreach (var form in entry.Forms.Keys)
        {
            if (!PluralRules.IsCategory(form))
            {
                Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath, entry.Line, entry.Column, name,
                    $"'{entry.Path}' has a form called '{form}', which is not a CLDR plural category "
                    + $"({string.Join(", ", PluralRules.Categories)})");
                return false;
            }

            if (System.Array.IndexOf(categories, form) < 0)
            {
                // Text the language can never select: a real mistake, and invisible at runtime.
                Report(spc, TranslationDiagnostics.Malformed, catalog.FilePath, entry.Line, entry.Column, name,
                    $"'{entry.Path}' has a '{form}' form, but {catalog.CultureTag} does not distinguish it "
                    + $"— it uses {string.Join("/", categories)}, so that text could never be shown");
                return false;
            }
        }

        return true;
    }

    private static void RenderPluralMember(
        StringBuilder sb,
        string pad,
        string name,
        string key,
        List<Catalog> catalogs,
        Catalog neutralCatalog)
    {
        var entry = neutralCatalog.Entries[key];
        var parameter = Escape(entry.PluralParameter!);
        var member = Escape(name);

        sb.AppendLine($"{pad}/// <summary><c>{neutralCatalog.CultureTag}</c>: a plural set counted by <c>{entry.PluralParameter}</c>.</summary>");
        sb.AppendLine($"{pad}public static string {member}(long {parameter})");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    var __format = __Index switch");
        sb.AppendLine($"{pad}    {{");

        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];
            if (ReferenceEquals(catalog, neutralCatalog)
                || !catalog.Entries.TryGetValue(key, out var localised)
                || !localised.IsPlural)
            {
                continue;
            }

            AppendArm(i.ToString(), catalog, localised);
        }

        // The neutral arm goes LAST, and is the reason this loop skips it above rather than emitting it
        // in catalog order like everything else. `_` matches everything, so an arm after it is
        // unreachable and the compiler says so (CS8510) — and catalogs are sorted by tag, so a neutral
        // that sorts first shadows every later language. "en" + "hu" is exactly that shape, which is
        // what `rask new --culture en --culture hu` scaffolds on every template.
        AppendArm("_", neutralCatalog, entry);

        sb.AppendLine($"{pad}    }};");

        void AppendArm(string arm, Catalog catalog, CatalogEntry localised)
        {
            sb.AppendLine($"{pad}        {arm} => __Plural.{PluralMethod(catalog.CultureTag)}({parameter}) switch");
            sb.AppendLine($"{pad}        {{");

            var categories = PluralRules.CategoriesFor(catalog.CultureTag)!;
            for (var c = 0; c < categories.Length; c++)
            {
                if (localised.Forms.TryGetValue(categories[c], out var form))
                {
                    sb.AppendLine($"{pad}            {c} => {Literal(MessageParser.Parse(form).Format)},");
                }
            }

            // The residual form is guaranteed present by ValidatePlural, so an unmatched category
            // still has text — and it is the language's own residual rather than a hardcoded "other".
            var residual = PluralRules.ResidualFor(catalog.CultureTag)!;
            sb.AppendLine($"{pad}            _ => {Literal(MessageParser.Parse(localised.Forms[residual]).Format)},");
            sb.AppendLine($"{pad}        }},");
        }
        sb.AppendLine($"{pad}    return string.Format(");
        sb.AppendLine($"{pad}        global::Rask.Core.Globalization.RaskCulture.Current, __format, {parameter});");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine();
    }

    private static string PluralMethod(string cultureTag)
    {
        var cut = cultureTag.IndexOf('-');
        var language = cut < 0 ? cultureTag : cultureTag.Substring(0, cut);
        return char.ToUpperInvariant(language[0]) + language.Substring(1).ToLowerInvariant();
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

    // One category function per language that has a plural key, and nothing at all for an app with
    // none. The arithmetic is CLDR's; see PluralRules for why it is a curated table.
    private static void RenderPluralFunctions(StringBuilder sb, List<Catalog> catalogs, Catalog neutralCatalog)
    {
        var needed = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var catalog in catalogs)
        {
            foreach (var key in catalog.Order)
            {
                if (catalog.Entries[key].IsPlural && PluralRules.BodyFor(catalog.CultureTag) is { } body)
                {
                    needed[PluralMethod(catalog.CultureTag)] = body;
                    break;
                }
            }
        }

        if (needed.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    // CLDR plural categories, as plain integer arithmetic. Emitted only for the");
        sb.AppendLine("    // languages this catalog actually pluralises in.");
        sb.AppendLine("    private static class __Plural");
        sb.AppendLine("    {");
        foreach (var pair in needed)
        {
            sb.AppendLine($"        internal static int {pair.Key}(long n)");
            sb.AppendLine("        {");
            sb.AppendLine($"            {pair.Value}");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
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
