using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// Keeps "this sample is the CLI's output" true.
/// </summary>
/// <remarks>
/// The README claims every file here came from <c>rask new</c> / <c>rask generate</c>. Claims rot. These
/// tests re-run the real generators and compare against the committed files, so if the CLI's output drifts
/// — or someone edits a generated file by hand — it shows up here instead of quietly becoming a lie.
/// Files the README lists as hand-written are exempt by name.
/// </remarks>
public sealed class ShopProvenanceTests
{
    private const string ProjectName = "Rask.Example.Shop";

    // What the README documents as written by hand, and why (see samples/Rask.Example.Shop/README.md).
    private static readonly HashSet<string> HandWritten = new(StringComparer.Ordinal)
    {
        "Features/Shared/DbInitializer.cs",   // a real app migrates; this one uses EnsureCreated so it just runs
        "Features/Orders/OrderCreatedHandler.cs", // the generator scaffolds a logging stub; this is the body
        "Features/Orders/OrderConfirmation.cs",   // ditto, for the email content
        "Features/Ops/OpsPage.cs",                // the hand-rolled dashboard over every pillar's table
        "Features/Ops/BackupProbe.cs",            // lights up the built-in dashboard's Backup card
        "Program.cs",                             // + the DbInitializer call and SnapshotOnStartup
        "Rask.Example.Shop.csproj",               // ProjectReference instead of PackageReference, in-repo
        "README.md",

        // Written by `rask new`, then EXTENDED by every `rask generate feature` (a DbSet + its using per
        // slice). Comparing it against a fresh `rask new` would only ever prove the features were added.
        // What matters about it — that each pillar's AddRaskX() schema call is present — is asserted in
        // Rask.Example.Shop.Tests.ShopPersistenceTests.Every_pillar_gets_its_table, against a real database.
        "Features/Shared/AppDbContext.cs",
    };

    /// <summary>
    /// Generated files this sample deliberately does not carry, as opposed to carries in a hand-written
    /// form. <c>rask new</c> writes project hygiene for a <em>standalone</em> app; this sample lives inside
    /// the Rask repo, which already supplies the ignore rules, the solution and the formatting config — and
    /// a nested <c>root = true</c> .editorconfig would override the repo's own rules for these very files,
    /// putting <c>dotnet format</c> at odds with itself.
    /// </summary>
    private static readonly HashSet<string> NotInSample = new(StringComparer.Ordinal)
    {
        ".gitignore",
        ".editorconfig",
        ProjectName + ".slnx",
    };

    private static string SampleDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "samples", ProjectName);
    }

    [Fact]
    public void The_scaffolded_files_still_match_what_rask_new_writes_today()
    {
        var generated = ProjectGenerator.GenerateServer(
            "/generated",
            ProjectName,
            // The Shop sample is written in Bs* components, so the provenance check has to ask for the
            // styling it actually uses. Plain CSS is the default now; it was Bootstrap when this was written.
            NewCommand.ToBatteries(["all-batteries", "auth", "docker"], Styling.Bootstrap),
            version: "0.0.0");

        var sampleDirectory = SampleDirectory();
        var checkedAny = false;

        foreach (var file in generated.Files)
        {
            var relative = Path.GetRelativePath("/generated", file.Path).Replace('\\', '/');
            if (HandWritten.Contains(relative) || NotInSample.Contains(relative))
            {
                continue;
            }

            var committed = Path.Combine(sampleDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(committed), $"{relative} is in the CLI's output but not in the sample.");

            Assert.Equal(
                Normalize(file.Content),
                Normalize(File.ReadAllText(committed)));
            checkedAny = true;
        }

        Assert.True(checkedAny, "The provenance check compared nothing — the exemption list has swallowed the sample.");
    }

    [Fact]
    public void Every_hand_written_exemption_still_exists()
    {
        // An exemption for a file that has since been deleted or renamed would silently stop checking a
        // file that IS generated.
        var sampleDirectory = SampleDirectory();

        foreach (var relative in HandWritten)
        {
            var path = Path.Combine(sampleDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{relative} is exempted as hand-written but doesn't exist.");
        }

        // The other exemption list is the mirror image: these must NOT be here, or the sample has picked
        // up a config that fights the repo's own.
        foreach (var relative in NotInSample)
        {
            var path = Path.Combine(sampleDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.False(File.Exists(path), $"{relative} is exempted as absent from the sample but exists.");
        }
    }

    // Line endings differ between what the generator returns and what git checked out; nothing else may.
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
