using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Rask.External.Tasks;

/// <summary>
///     Which <c>.ts</c> files beside a <c>.cs</c> are ISLAND modules, read out of the C# source before
///     it is compiled.
/// </summary>
/// <remarks>
///     <para>
///         A Lit (or Angular) island is declared by dropping <c>Name.ts</c> beside <c>Name.cs</c>, which
///         is character-for-character the rule Rask's scoped TypeScript already uses. The only
///         difference is whether the class in <c>Name.cs</c> derives from <c>LitComponent</c> — a fact
///         Roslyn knows and a glob does not, so with both features in one project they claimed each
///         other's files in both directions (#938).
///     </para>
///     <para>
///         The compiled assembly already carries the answer, and <see cref="ExternalIslandMetadata" />
///         lifts it back out. That is too late for HALF the problem: the scoped-TypeScript pipeline
///         compiles its files and hands the output to csc as <c>AdditionalFiles</c>, so its file list
///         has to be right BEFORE the compile that would produce the assembly. There is no ordering
///         that gives an MSBuild target the assembly it needs, and reusing the previous build's would
///         make a clean build differ from the second one.
///     </para>
///     <para>
///         So the fact is read where it is first written: the C# source. A base list is syntax, not
///         semantics — <c>class Gauge : LitComponent</c> says what it says with no references resolved
///         — which is why a scan this cheap can answer it at all. Comments are stripped first, the base
///         chain is followed through the project's own intermediate classes, and only a class whose
///         runtime writes a plain <c>.ts</c> claims one.
///     </para>
///     <para>
///         It is an APPROXIMATION of Roslyn, and it is checked against Roslyn on the same build:
///         <c>WriteExternalPropTypesTask.ReportUnbuiltIslands</c> reads the real island list out of the
///         compiled assembly and warns about any island whose front-end file the build did not claim.
///         A miss here is therefore reported rather than silent — and in this repository, which builds
///         with <c>-warnaserror</c>, it stops the build.
///     </para>
/// </remarks>
public static class ExternalSourceScan
{
    /// <summary>
    ///     The base class that declares each runtime, by simple name, and the extension its module has.
    /// </summary>
    /// <remarks>
    ///     Mirrors the table in <c>ExternalGenerator</c>, which is the authority. Every runtime is
    ///     listed rather than only the two that write <c>.ts</c>: a <c>.ts</c> beside a
    ///     <c>Chart : ReactComponent</c> is NOT that component's module (its module is
    ///     <c>./Chart.tsx</c>), and knowing which runtime claimed the name is what lets this say so
    ///     instead of guessing.
    /// </remarks>
    private static readonly Dictionary<string, (string Runtime, string Extension)> Bases =
        new(StringComparer.Ordinal)
        {
            ["ReactComponent"] = ("react", ".tsx"),
            ["PreactComponent"] = ("preact", ".tsx"),
            ["SolidComponent"] = ("solid", ".tsx"),
            ["LitComponent"] = ("lit", ".ts"),
            ["AngularComponent"] = ("angular", ".ts"),
            ["VueComponent"] = ("vue", ".vue"),
            ["SvelteComponent"] = ("svelte", ".svelte"),
        };

    /// <summary>
    ///     A class declaration with a base list, as it is written.
    /// </summary>
    /// <remarks>
    ///     The optional <c>(…)</c> is a primary constructor, which sits between the name and the base
    ///     list; without it <c>class Gauge(int x) : LitComponent</c> matches nothing and its island
    ///     silently becomes a scoped asset. The first base is the only one taken — C# requires the base
    ///     class to come first, and everything after it is an interface.
    /// </remarks>
    private static readonly Regex Declaration = new(
        @"(?<modifiers>(?:\b(?:public|internal|private|protected|sealed|abstract|static|partial|file|unsafe|record|new)\s+)*)"
        + @"\bclass\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*(?:\([^()]*\))?\s*:\s*(?<base>[A-Za-z_][\w.]*)",
        RegexOptions.CultureInvariant);

    /// <summary>Line and block comments, removed before the declarations are read.</summary>
    /// <remarks>
    ///     Every file in this repository documents its own components, and a doc comment quoting
    ///     <c>class Gauge : LitComponent</c> would otherwise declare an island nobody wrote. Stripping
    ///     reaches inside string literals too, which can only ever cost a match — never invent one.
    /// </remarks>
    private static readonly Regex Comments = new(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline);

    /// <summary>A base chain longer than this is a cycle, or source this has no business reading.</summary>
    private const int MaxDepth = 32;

    /// <summary>
    ///     Each concrete class that derives from an external component base, mapped to its runtime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Keyed by SIMPLE name, which is the pairing rule everywhere else in this feature and the
    ///         key the browser resolves a module by; <c>RASK058</c> already refuses two islands sharing
    ///         one.
    ///     </para>
    ///     <para>
    ///         Abstract classes are kept in the chain and left out of the result: a project's own
    ///         <c>abstract class Widget : LitComponent</c> is what makes its concrete subclasses
    ///         islands, and it has no module of its own.
    ///     </para>
    /// </remarks>
    /// <param name="sources">The project's C# files. Unreadable ones are skipped, not fatal.</param>
    public static IReadOnlyDictionary<string, string> IslandRuntimes(IEnumerable<string> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var baseOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var abstractTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in sources)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // Nothing in this file can declare a class, so the regex is not worth running on it.
            //
            // Deliberately NOT a test for "Component": the leaf of a chain need not mention it. A
            // project's own `abstract class Widget : LitComponent` carries the runtime down to a
            // `Dial : Widget` whose file names no base of Rask's at all, and skipping that file left
            // the island silently compiled as a scoped asset.
            if (text.IndexOf("class", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            foreach (Match match in Declaration.Matches(Comments.Replace(text, " ")))
            {
                var name = match.Groups["name"].Value;
                var declaredBase = LastSegment(match.Groups["base"].Value);

                // A partial class can spell its base list in one part and not the others, so the
                // first part that names one wins rather than the last file enumerated.
                if (!baseOf.ContainsKey(name))
                {
                    baseOf[name] = declaredBase;
                }

                if (match.Groups["modifiers"].Value.IndexOf("abstract", StringComparison.Ordinal) >= 0)
                {
                    abstractTypes.Add(name);
                }
            }
        }

        var runtimes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in baseOf)
        {
            if (abstractTypes.Contains(pair.Key))
            {
                continue;
            }

            if (Resolve(baseOf, pair.Key) is { } runtime)
            {
                runtimes[pair.Key] = runtime.Runtime;
            }
        }

        return runtimes;
    }

    /// <summary>
    ///     The runtime of the island whose module <paramref name="frontEndFile" /> is, or null when no
    ///     island claims it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three things have to hold, and each of them is a way the two conventions collided. The
    ///         file's name has to be a class the scan found; a <c>.cs</c> of that name has to sit in the
    ///         SAME directory, which is the documented pairing rule; and the class's runtime has to be
    ///         one whose module carries this extension, so the <c>Chart.ts</c> beside a
    ///         <c>Chart : ReactComponent</c> stays a scoped asset — that component's module is
    ///         <c>./Chart.tsx</c>.
    ///     </para>
    ///     <para>
    ///         A component overriding <c>Module</c> to name something else entirely is not detected
    ///         here; its sibling file is claimed as if it were the module. The build says so: the
    ///         declared module has no file, so <c>ReportUnbuiltIslands</c> warns about it.
    ///     </para>
    /// </remarks>
    public static string? RuntimeOfModule(IReadOnlyDictionary<string, string> runtimes, string frontEndFile)
    {
        if (runtimes is null)
        {
            throw new ArgumentNullException(nameof(runtimes));
        }

        if (string.IsNullOrEmpty(frontEndFile))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(frontEndFile);
        if (string.IsNullOrEmpty(name) || !runtimes.TryGetValue(name, out var runtime))
        {
            return null;
        }

        if (!File.Exists(Path.ChangeExtension(frontEndFile, ".cs")))
        {
            return null;
        }

        foreach (var declared in Bases.Values)
        {
            if (string.Equals(declared.Runtime, runtime, StringComparison.Ordinal))
            {
                return string.Equals(
                    Path.GetExtension(frontEndFile), declared.Extension, StringComparison.OrdinalIgnoreCase)
                    ? runtime
                    : null;
            }
        }

        return null;
    }

    /// <summary>The runtime a class reaches by following its base chain, or null if it reaches none.</summary>
    private static (string Runtime, string Extension)? Resolve(
        IReadOnlyDictionary<string, string> baseOf,
        string name)
    {
        var current = name;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (!baseOf.TryGetValue(current, out var declaredBase))
            {
                return null;
            }

            if (Bases.TryGetValue(declaredBase, out var runtime))
            {
                return runtime;
            }

            current = declaredBase;
        }

        return null;
    }

    /// <summary>
    ///     The last dotted segment of a base name, so <c>Rask.External.LitComponent</c> and
    ///     <c>LitComponent</c> are one answer.
    /// </summary>
    /// <remarks>
    ///     A qualified base that merely ENDS in one of these names is treated as that base. The cost of
    ///     being wrong is bounded and loud — the file is offered to the bundler and the assembly read
    ///     that follows finds no island of that name — where the cost of missing an ordinary
    ///     <c>global::Rask.External.LitComponent</c> is an island silently compiled as a scoped asset.
    /// </remarks>
    private static string LastSegment(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name.Substring(dot + 1);
    }
}
