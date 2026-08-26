using System.Security.Cryptography;
using System.Text;
using Rask.Cli.Commands;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     Every <c>--template</c> key the parser accepts produces its own project, and no two produce the
///     same one.
/// </summary>
/// <remarks>
///     <para>
///         This is the guard #830 asked for, and it is aimed at the *mechanism* of that bug rather than
///         its name. <c>TemplateCatalog</c> advertised a <c>native</c> template after the native host was
///         deleted; <c>NewCommand</c>'s switch had no <c>native</c> arm, so it fell through to
///         <c>_ =&gt; GenerateServer(...)</c>. The parser accepted the value, the CLI announced
///         "Creating Rask native mobile app…", and wrote a server-rendered app. Nothing threw, so nothing
///         caught it.
///     </para>
///     <para>
///         The existing tests assert that <c>native</c> specifically is gone. That is worth having, but it
///         is keyed on a string: the same fall-through can recur under any new key. What actually
///         characterises the bug is that a template produced output *identical to another template's* —
///         so that is what this asserts, and it needs to know nothing about what each template should
///         contain. A key with no generator arm lands on the default and is caught by construction.
///     </para>
///     <para>
///         Both halves matter. "Every key produces something" catches a key wired to nothing; "no two keys
///         produce the same thing" catches a key wired to the wrong thing, which is the shape that shipped.
///     </para>
///     <para>
///         Proven by failing it, not just by passing: adding a <c>mobile</c> entry to the catalog with no
///         arm in the switch — the exact shape of #830 — turns this red with
///         <c>--template mobile produces exactly the same project as --template server</c>. A guard for a
///         bug that no longer exists is worth only what it does when the bug comes back.
///     </para>
/// </remarks>
public sealed class TemplateParityTests
{
    private const string WorkingDirectory = "/proj";

    public static TheoryData<string> AcceptedTemplateKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in TemplateCatalog.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AcceptedTemplateKeys))]
    public async Task Every_accepted_template_key_scaffolds_something(string key)
    {
        var (console, fingerprint) = await ScaffoldAsync(key);

        Assert.True(
            fingerprint.Length > 0,
            $"--template {key} is accepted by the parser but produced no files and ran no scaffolder. "
            + $"stderr: {console}");
    }

    [Fact]
    public async Task No_two_template_keys_produce_the_same_project()
    {
        var byFingerprint = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in TemplateCatalog.Keys)
        {
            var (_, fingerprint) = await ScaffoldAsync(key);
            var hash = Hash(fingerprint);

            Assert.False(
                byFingerprint.TryGetValue(hash, out var twin),
                $"--template {key} produces exactly the same project as --template {twin}. That is what a "
                + "key with no arm in NewCommand's generator switch looks like: the parser accepts it, the "
                + "CLI reports creating it, and the default arm writes something else. It is how "
                + "'--template native' shipped as a server app (#830).");

            byFingerprint[hash] = key;
        }

        Assert.Equal(TemplateCatalog.Keys.Count, byFingerprint.Count);
    }

    /// <summary>
    ///     Drives the real <see cref="Commands.NewCommand"/>, so the generator switch under test is the one
    ///     that ships. Returns the console's error text and a stable description of everything the run
    ///     produced — the files it wrote, and the external scaffolders it invoked.
    /// </summary>
    /// <remarks>
    ///     The invocations are part of the fingerprint because a front-end template's identity partly lives
    ///     there: `create-vite --template react` versus `--template preact` is the difference between two
    ///     of these templates, and the files either writes can be identical.
    /// </remarks>
    private static async Task<(string Console, string Fingerprint)> ScaffoldAsync(string key)
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner();
        var command = new NewCommand(console, fs, runner, WorkingDirectory);

        await command.ExecuteAsync(["App", "--template", key], CancellationToken.None);

        var builder = new StringBuilder();
        foreach (var file in fs.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            builder.Append(file.Key).Append('\0').Append(Hash(file.Value)).Append('\n');
        }

        foreach (var invocation in runner.Invocations)
        {
            builder.Append("run\0").Append(string.Join(' ', invocation.Arguments)).Append('\n');
        }

        return (console.ErrorText, builder.ToString());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
