using System.Text;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     The committed template trees reproduce, byte for byte, everything the hand-written generators
///     write — for every template and every battery combination.
/// </summary>
/// <remarks>
///     <para>
///         This is the proof that moving the scaffolder's content out of C# string literals and into
///         <c>src/Rask.Templates/</c> changed nothing about what <c>rask new</c> produces. It is written
///         to be deleted: once the generators are gone there is no second implementation to compare
///         against, and what survives is the scaffold-and-build gate.
///     </para>
///     <para>
///         Both sides run in-process and are handed the SAME <see cref="ServerBatteries"/>, computed by
///         the real <see cref="NewCommand.ToBatteries"/>. Nothing is inferred from the output about which
///         batteries were on, which is the trap an earlier version of this check fell into: once most
///         regions are conjunctions there is no line that belongs to exactly one flag, so probing the
///         result tells you what the thing under test already decided.
///     </para>
///     <para>
///         The front-end templates are expected to produce MORE files than their generator does. That is
///         the point of the change: those files used to come from <c>npx create-vite@latest</c> at
///         scaffold time and are now committed, so the assertion is that every file the generator writes
///         is reproduced identically, not that the two sets are equal.
///     </para>
/// </remarks>
public sealed class TemplateMaterializerEquivalenceTests
{
    private const string Name = "Company.RaskServer";
    private const string Version = "9.9.9";
    private const string Target = "/proj/Company.RaskServer";

    public static TheoryData<string, string> TemplatesAndOptOuts()
    {
        var data = new TheoryData<string, string>();
        foreach (var template in TemplateCatalog.All)
        {
            var batteries = template.SupportedFlags.Where(f => f != "wasm").Order(StringComparer.Ordinal).ToArray();

            data.Add(template.Key, "");                                  // everything on
            data.Add(template.Key, string.Join(' ', batteries));         // everything off

            foreach (var flag in batteries)
            {
                data.Add(template.Key, flag);                            // one at a time
            }

            // A couple of interactions, because a flag can be right alone and wrong beside another.
            if (batteries.Length >= 4)
            {
                data.Add(template.Key, string.Join(' ', batteries.Take(2)));
                data.Add(template.Key, string.Join(' ', batteries.Where((_, i) => i % 2 == 0)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TemplatesAndOptOuts))]
    public void Template_reproduces_every_file_the_generator_writes(string key, string offList)
    {
        var off = offList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(TemplateCatalog.TryGet(key, out var template));

        // --wasm is opt-in, so the maximal shape asks for it wherever the template accepts it. The
        // template trees are extracted against that same maximal baseline.
        var wasm = template.SupportedFlags.Contains("wasm") && !off.Contains("wasm");
        var batteries = NewCommand.ToBatteries(template, off, wasm);

        var expected = GeneratedFiles(key, batteries);
        var actual = TemplateMaterializer
            .Files(Target, key, Name, batteries, Version)
            .ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

        var missing = expected.Keys.Where(p => !actual.ContainsKey(p)).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            missing.Length == 0,
            $"--template {key} (off: {offList}) — the template tree does not produce "
            + $"{missing.Length} file(s) the generator does:\n  {string.Join("\n  ", missing)}");

        var wrong = new StringBuilder();
        foreach (var (path, content) in expected.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!string.Equals(Normalize(actual[path]), Normalize(content), StringComparison.Ordinal))
            {
                wrong.Append(path).Append('\n').Append(FirstDifference(content, actual[path])).Append('\n');
            }
        }

        Assert.True(wrong.Length == 0, $"--template {key} (off: {offList}) differs:\n{wrong}");
    }

    /// <summary>
    ///     What the hand-written generator produces, through the same dispatch <see cref="NewCommand"/>
    ///     uses so the arm under test is the one that ships.
    /// </summary>
    private static Dictionary<string, string> GeneratedFiles(string key, ServerBatteries batteries)
    {
        var result = SpaFramework.TryGet(key, out var spa)
            ? ProjectGenerator.GenerateSpa(Target, Name, spa, batteries, Version)
            : MetaTemplate.TryGet(key, out var meta)
                ? ProjectGenerator.GenerateMeta(Target, Name, meta, batteries, Version)
                : key switch
                {
                    "wasm" => ProjectGenerator.GenerateWasm(
                        Target, Name, batteries.Pwa, batteries.Docker, Version),
                    _ => ProjectGenerator.GenerateServer(Target, Name, batteries, Version),
                };

        return result.Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Line endings and a trailing newline are not part of what is being compared: the generators
    ///     build strings with raw literals and the templates are files on disk, so the two disagree about
    ///     the final newline in ways no scaffolded project can tell apart.
    /// </summary>
    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string FirstDifference(string expected, string actual)
    {
        var e = Normalize(expected).Split('\n');
        var a = Normalize(actual).Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var left = i < e.Length ? e[i] : "<end of file>";
            var right = i < a.Length ? a[i] : "<end of file>";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return $"  line {i + 1}\n    generator: {left}\n    template:  {right}";
            }
        }

        return "  (identical once normalized)";
    }
}
