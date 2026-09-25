namespace Rask.Generators.Tests;

// An element type is named after its DOM interface and reached by entries named after its tags: [Tag] is
// what joins the two. A type several tags share gets one entry per tag, each stamping the tag it built.
public class TagEntryTests
{
    private const string Entries = "RaskBuilderEntryHost.g.cs";
    private const string Tags = "RaskTags.g.cs";

    private const string Source = """
                                  using Rask.Core;
                                  namespace Rask.Core
                                  {
                                      [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                                      internal sealed class TagAttribute(string name) : System.Attribute
                                      {
                                          public string Name { get; } = name;
                                          public string? Entry { get; set; }
                                      }
                                  }
                                  namespace Demo
                                  {
                                      [Tag("h1")]
                                      [Tag("h2")]
                                      public partial class Heading : Element { }
                                      [Tag("hr")]
                                      public sealed partial class Rule : Element { }
                                      [Tag("object", Entry = "Embedded")]
                                      public sealed partial class ObjectElement : Element { }
                                  }
                                  """;

    [Fact]
    public void A_type_several_tags_share_gets_one_entry_per_tag_that_stamps_its_tag()
    {
        var run = BuilderGeneratorHarness.Run(Source);

        var entries = run.Source(Entries);

        Assert.Contains("global::Demo.Heading H1 => global::Rask.Core.BuilderRuntime.Tag(", entries, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.RaskTags.Id_h2)", entries, StringComparison.Ordinal);
        Assert.DoesNotContain(" Heading =>", entries, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_with_one_tag_is_reached_by_its_tag_and_stamps_it_in_its_constructor()
    {
        var run = BuilderGeneratorHarness.Run(Source);

        var entries = run.Source(Entries);
        var tags = run.Source(Tags);

        Assert.Contains("global::Demo.Rule Hr => global::Rask.Core.BuilderRuntime.Entry<", entries, StringComparison.Ordinal);
        Assert.Contains("global::Demo.ObjectElement Embedded =>", entries, StringComparison.Ordinal);
        Assert.Contains("public Rule() => TagId = global::Rask.Core.RaskTags.Id_hr;", tags, StringComparison.Ordinal);
        Assert.DoesNotContain("public Heading()", tags, StringComparison.Ordinal);
    }
}
