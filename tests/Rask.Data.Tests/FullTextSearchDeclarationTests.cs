using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// The provider-neutral half of full-text search: what HasFullTextSearch records, what Search(text) turns typed text
// into, and what the markers do when nothing translates them. The SQLite half is covered end to end in
// Rask.SQLite.EntityFrameworkCore.Tests.
[Collection(DataDbCollection.Name)]
public sealed class FullTextSearchDeclarationTests
{
    [Theory]
    [InlineData("sqlite", "\"sqlite\" *")]
    [InlineData("  fast   sqlite ", "\"fast\" \"sqlite\" *")]
    [InlineData("say \"hi\"", "\"say\" \"\"\"hi\"\"\" *")]
    [InlineData("C# - intro", "\"C#\" \"intro\" *")]
    [InlineData("NEAR(a b)", "\"NEAR(a\" \"b)\" *")]
    [InlineData("title:x OR y", "\"title:x\" \"OR\" \"y\" *")]
    public void Typed_text_becomes_quoted_words_with_a_prefix_on_the_last(string text, string expected) =>
        Assert.Equal(expected, FullTextQuery.Compile(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n")]
    [InlineData("-- ** \"\" ()")]
    public void Text_without_a_word_compiles_to_nothing(string? text) => Assert.Null(FullTextQuery.Compile(text));

    [Fact]
    public void The_number_of_words_is_bounded()
    {
        var text = string.Join(' ', Enumerable.Range(0, 100).Select(i => $"w{i}"));

        Assert.Equal(FullTextQuery.MaxTerms, FullTextQuery.Compile(text)!.Split('"', StringSplitOptions.RemoveEmptyEntries)
            .Count(part => part.StartsWith('w')));
    }

    [Fact]
    public void HasFullTextSearch_records_the_properties_in_order_and_the_tokenizer()
    {
        var entity = BuildModel(b => b.Entity<Doc>().HasFullTextSearch(d => new { d.Body, d.Title }, FullTextTokenizer.English))
            .FindEntityType(typeof(Doc))!;

        Assert.True(FullTextSearchSpec.TryParse(entity.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec));
        Assert.Equal(["Body", "Title"], spec.Properties);
        Assert.Equal(FullTextTokenizer.English, spec.Tokenizer);
    }

    [Fact]
    public void The_spec_round_trips_and_compares_by_content()
    {
        var spec = new FullTextSearchSpec(["Title", "Body"], FullTextTokenizer.Unicode);

        Assert.True(FullTextSearchSpec.TryParse(spec.Serialize(), out var parsed));
        Assert.Equal(spec, parsed);
        Assert.Equal(spec.GetHashCode(), parsed.GetHashCode());
        Assert.False(FullTextSearchSpec.TryParse("v2|Title|Unicode", out _));
        Assert.False(FullTextSearchSpec.TryParse("v1||Unicode", out _));
        Assert.False(FullTextSearchSpec.TryParse("v1|Title|Klingon", out _));
    }

    [Fact]
    public void HasFullTextSearch_refuses_what_it_cannot_index()
    {
        Assert.Throws<ArgumentException>(() => BuildModel(b => b.Entity<Doc>().HasFullTextSearch(d => d.Title.ToUpperInvariant())));
        Assert.Throws<ArgumentException>(() => BuildModel(b => b.Entity<Doc>().HasFullTextSearch(d => new { d.Title, Again = d.Title })));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildModel(b => b.Entity<Doc>().HasFullTextSearch(d => d.Title, (FullTextTokenizer)42)));
    }

    [Fact]
    public void Search_with_no_word_is_the_same_query()
    {
        var source = new List<Doc>().AsQueryable();

        Assert.Same(source, source.Search("  "));
        Assert.Same(source, source.Search(null));
    }

    [Fact]
    public void Search_on_a_provider_that_cannot_translate_it_says_what_to_configure()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new List<Doc>().AsQueryable().Search("x").ToList());

        Assert.Contains("UseRaskSqlite(services)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_highlight_markers_explain_themselves_outside_a_query()
    {
        var error = Assert.Throws<InvalidOperationException>(() => FullText.Highlight("x"));

        Assert.Contains("Post.Search(text).Select(p => FullText.Highlight(p.Title))", error.Message, StringComparison.Ordinal);
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel(Action<ModelBuilder> configure)
    {
        var options = new DbContextOptionsBuilder<DocContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new DocContext(options, configure);
        return db.Model;
    }

    public sealed class Doc
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;
    }

    // A fresh model per call: the configuration varies, so EF's per-type model cache must not be shared.
    private sealed class DocContext(DbContextOptions<DocContext> options, Action<ModelBuilder> configure) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => configure(modelBuilder);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, NoCache>();
    }

    private sealed class NoCache : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => new object();
    }
}
