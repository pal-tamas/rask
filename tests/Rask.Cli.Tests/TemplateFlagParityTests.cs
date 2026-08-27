using Rask.Cli.Commands;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
/// The guard for this repository's most expensive bug class: a flag the CLI accepts and then disregards.
/// </summary>
/// <remarks>
/// <c>--template native</c> is the one everybody remembers — it survived the native host's deletion and
/// went on scaffolding a server app. <c>--localization</c> on the browser-WASM templates was the same
/// shape and lived longer precisely because it was quieter: both templates advertised it, took a
/// <c>--culture</c>, reported success, and generated no catalogs and no negotiation, because neither
/// generator ever read the property.
///
/// <para>
/// Neither of those is reachable by a test that asserts a template supports a flag, which is what the
/// suite had — asserting PRESENCE is how the bug survived review. The invariant that catches both is
/// about the <em>output</em>, and it is stated once here for every pair rather than per feature:
/// </para>
///
/// <para>
/// <b>For every template and every flag it advertises, flipping that flag either changes the project it
/// generates or is refused as a usage error. It is never accepted and quietly ignored.</b>
/// </para>
///
/// <para>
/// A refusal counts as passing on purpose: <c>--no-cqrs</c> on a TypeScript front end is rejected because
/// the generated client dispatches through the mediator, and being told so is the opposite of the failure
/// this guards against. What cannot happen is exit 0 with byte-identical output.
/// </para>
/// </remarks>
public sealed class TemplateFlagParityTests
{
    /// <summary>Every (template, flag) pair the catalog advertises.</summary>
    public static TheoryData<string, string> AdvertisedFlags()
    {
        var data = new TheoryData<string, string>();
        foreach (var template in TemplateCatalog.All)
        {
            foreach (var flag in template.SupportedFlags.OrderBy(f => f, StringComparer.Ordinal))
            {
                data.Add(template.Key, flag);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AdvertisedFlags))]
    public async Task Flipping_an_advertised_flag_is_never_a_no_op(string templateKey, string flag)
    {
        var (baseExit, baseline) = await ScaffoldAsync(templateKey, []);
        Assert.Equal(0, baseExit);
        Assert.NotEmpty(baseline);

        var (flippedExit, flipped) = await ScaffoldAsync(templateKey, Flip(templateKey, flag));

        if (flippedExit != 0)
        {
            // Refused rather than honoured — the flag still means something, and the user was told.
            return;
        }

        Assert.False(
            SameProject(baseline, flipped),
            $"--template {templateKey} advertises '{flag}', but {string.Join(' ', Flip(templateKey, flag))} "
            + "generated a byte-identical project. A flag that is accepted and then disregarded is the "
            + "bug --template native and --localization-on-wasm both were.");
    }

    /// <summary>
    /// The argument that flips <paramref name="flag"/> away from what this template does by default.
    /// </summary>
    /// <remarks>
    /// Which direction that is depends on the template, which is the whole point of
    /// <see cref="TemplateInfo.OptInFlags"/>: auth is off everywhere, localization is standard on
    /// <c>server</c> and opt-in on the browser templates, and everything else is standard wherever it is
    /// advertised. Localization's opt-in is spelled <c>--culture</c> rather than <c>--localization</c>,
    /// because naming a language is the thing you actually want to say.
    /// </remarks>
    private static string[] Flip(string templateKey, string flag)
    {
        _ = TemplateCatalog.TryGet(templateKey, out var template);

        if (flag == "auth")
        {
            return ["--auth"];
        }

        if (!template.OptInFlags.Contains(flag))
        {
            return ["--no-" + flag];
        }

        return flag == "localization" ? ["--culture", "hu"] : ["--" + flag];
    }

    private static async Task<(int Exit, IReadOnlyDictionary<string, string> Files)> ScaffoldAsync(
        string templateKey, IReadOnlyList<string> extra)
    {
        var fs = new FakeFileSystem();
        var command = new NewCommand(new StringConsole(), fs, new FakeProcessRunner(), "/proj");

        // --no-restore/--no-git keep this to what the generators write; the point of comparison is the
        // project on disk, not the tooling run over it afterwards.
        string[] args = ["App", "--template", templateKey, "--no-restore", "--no-git", .. extra];
        var exit = await command.ExecuteAsync(args, CancellationToken.None);

        return (exit, fs.Files.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));
    }

    private static bool SameProject(
        IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(entry =>
            right.TryGetValue(entry.Key, out var content)
            && string.Equals(content, entry.Value, StringComparison.Ordinal));
}
