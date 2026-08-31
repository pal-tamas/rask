using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Playground.Compiler;
using Rask.Testing;

namespace Rask.Example.Playground.Tests;

// Guards the guided tutorial track. Every chapter must compile under the same reference set the browser
// ships, and the data chapters must actually reach SQLite — create the database, save, and query it back.
// Running them here on the desktop means a broken chapter fails the build instead of greeting a reader with
// red squiggles (or an empty list) on the deployed site.
public sealed class TutorialChaptersTests
{
    public static TheoryData<string> ChapterIds()
    {
        var data = new TheoryData<string>();
        foreach (var chapter in TutorialChapters.All)
        {
            data.Add(chapter.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ChapterIds))]
    public async Task Chapter_compiles_and_renders(string id)
    {
        var chapter = TutorialChapters.All.Single(c => c.Id == id);
        ResetChapterDatabases();

        var result = await Compile(chapter);

        Assert.True(result.Succeeded, Dump(chapter, result));
        Assert.NotNull(result.Component);
        Assert.NotEmpty(RaskTest.Render(result.Component!).Html);
    }

    // The proof that the data story works end to end: each of these seeds and queries a real SQLite
    // database from OnMountAsync, so the expected row can only appear if EF Core round-tripped it.
    [Theory]
    [InlineData("query", "Cold brew")]
    [InlineData("edit-delete", "Flat white")]
    [InlineData("relationships", "Ada")]
    public async Task Data_chapter_seeds_and_queries_real_sqlite(string id, string expected)
    {
        var chapter = TutorialChapters.All.Single(c => c.Id == id);
        Assert.True(chapter.NeedsDatabase);
        ResetChapterDatabases();

        var result = await Compile(chapter);
        Assert.True(result.Succeeded, Dump(chapter, result));

        var page = RaskTest.Render(result.Component!);

        // The load happens in OnMountAsync, so the row lands on a later paint than the first.
        await page.WaitForAsync(html => html.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Entity_chapter_inserts_a_row_when_the_button_is_clicked()
    {
        var chapter = TutorialChapters.All.Single(c => c.Id == "entity");
        ResetChapterDatabases();

        var result = await Compile(chapter);
        Assert.True(result.Succeeded, Dump(chapter, result));

        var page = RaskTest.Render(result.Component!);

        // A fresh database: EnsureCreated ran, and the table is empty.
        await page.WaitForAsync(html => html.Contains("No rows yet", StringComparison.Ordinal));

        await page.On(".action").ClickAsync();

        await page.WaitForAsync(html => html.Contains("Espresso", StringComparison.Ordinal));
        Assert.Contains("1 row(s)", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Chapters_are_numbered_contiguously_from_one_with_unique_ids()
    {
        Assert.NotEmpty(TutorialChapters.All);
        Assert.Equal(
            Enumerable.Range(1, TutorialChapters.All.Count),
            TutorialChapters.All.Select(c => c.Number));
        Assert.Equal(
            TutorialChapters.All.Count,
            TutorialChapters.All.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Same(TutorialChapters.All[0], TutorialChapters.First);
    }

    [Fact]
    public void Every_chapter_declares_the_entry_component_and_its_own_namespace()
    {
        Assert.All(TutorialChapters.All, chapter =>
        {
            Assert.Contains("class Playground : Component", chapter.Code, StringComparison.Ordinal);
            Assert.Contains("namespace Demo;", chapter.Code, StringComparison.Ordinal);
            Assert.NotEmpty(chapter.Steps);
            Assert.NotEmpty(chapter.Goal);
        });
    }

    // NeedsDatabase drives whether the UI offers the chapter at all on a build without the SQLite
    // packages, so it has to match what the snippet actually does.
    [Fact]
    public void NeedsDatabase_matches_whether_the_chapter_opens_a_DbContext()
    {
        Assert.All(TutorialChapters.All, chapter =>
            Assert.Equal(chapter.Code.Contains("DbContext", StringComparison.Ordinal), chapter.NeedsDatabase));
    }

    // The chapter's connection string and PlaygroundView's Reset have to agree on the file name, and the
    // only thing tying them together is this convention — the code is a string, so nothing else would.
    [Fact]
    public void Each_data_chapter_owns_the_database_file_named_after_its_number()
    {
        foreach (var chapter in TutorialChapters.All.Where(c => c.NeedsDatabase))
        {
            var expected = $"Data Source=ch{chapter.Number.ToString(CultureInfo.InvariantCulture)}.db";

            Assert.True(
                chapter.Code.Contains(expected, StringComparison.Ordinal),
                $"Chapter {chapter.Number} must use '{expected}' — PlaygroundView.DeleteChapterDatabase "
                + "derives the file name from the chapter number, so Reset would otherwise clear the wrong "
                + "database (or none).");

            // Pooling stays off: a chapter may drop and recreate its database between runs, and a pooled
            // connection would keep serving the deleted file ("table already exists" on the second Run).
            Assert.Contains("Pooling=False", chapter.Code, StringComparison.Ordinal);
        }
    }

    // Built once: TestReferences.Build() reads several hundred assemblies off disk (the whole
    // trusted-platform set, EF Core included), and this class alone would otherwise repeat that a dozen
    // times in the gate the pre-commit hook blocks every commit on.
    private static readonly Lazy<ImmutableArray<MetadataReference>> _references = new(TestReferences.Build);

    private static Task<PlaygroundResult> Compile(TutorialChapter chapter) =>
        new PlaygroundCompiler(_references.Value, new ServiceCollection().BuildServiceProvider())
            .CompileAsync(chapter.Code);

    // Chapters address their database by a relative path, which is the browser's in-memory filesystem root
    // at runtime and this test's working directory here. Clear them so every case starts from nothing.
    private static void ResetChapterDatabases()
    {
        foreach (var path in Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "ch?.db"))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A previous case's OnMountAsync is fire-and-forget, so its connection can still hold the
                // file (on Windows that makes Delete throw). The chapters that care recreate their own
                // database on mount anyway — failing the arrange step over this would be a phantom.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }

    private static string Dump(TutorialChapter chapter, PlaygroundResult result) =>
        $"Chapter {chapter.Number} ('{chapter.Title}') failed to compile:\n" + string.Join("\n",
            result.Diagnostics
                .Where(d => d.Severity == PlaygroundSeverity.Error)
                .Select(d => $"  {d.Id} ({d.StartLine},{d.StartColumn}): {d.Message}"));
}
