namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract for the two places the client turns text from a frame into something the browser
///     acts on. Structural for the reason <see cref="BuildStatusClientContractTests" /> gives: <c>rask.ts</c> boots a
///     socket against a live document and cannot be loaded in Node.
/// </summary>
public class ClientUrlSafetyContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    [Fact]
    public void A_location_frame_only_ever_navigates_to_a_page_on_this_origin()
    {
        // Followed as given, a frame naming javascript:… would run as script, and an absolute URL would take the
        // visitor off the site. The check has to come before the page moves, not after.
        var js = Read("src", "Rask.Server", "Resources", "rask.ts");
        var branch = js[js.IndexOf("data.type === \"location\"", StringComparison.Ordinal)..];
        branch = branch[..branch.IndexOf("return;\n        }", StringComparison.Ordinal)];

        var check = branch.IndexOf("target.origin !== location.origin", StringComparison.Ordinal);

        Assert.True(check >= 0, "the location frame no longer checks the target's origin");
        Assert.True(check < branch.IndexOf("location.assign(", StringComparison.Ordinal));
        Assert.True(check < branch.IndexOf("location.replace(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dev_error_reaches_the_console_as_text_never_as_a_format_string()
    {
        // console.error reads %c, %o and %s in its first argument as directives, so an error title carrying them
        // would restyle or reshape the log line instead of appearing as written.
        var js = Read("src", "Rask.Core", "Resources", "rask-deverror.ts");

        Assert.Contains("console.error(\"%s\", \"[Rask] \"", js, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { _repoRoot }.Concat(parts).ToArray())).ReplaceLineEndings("\n");

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate the repository root (Rask.slnx).");
    }
}
