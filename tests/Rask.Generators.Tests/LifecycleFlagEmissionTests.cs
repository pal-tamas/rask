namespace Rask.Generators.Tests;

// An entry tells the runtime whether its component has a lifecycle to run, by writing `, false` when it has
// none. Get that wrong the quiet way and a handle-less render never calls the component's Mount. The hooks
// used to share an `On` prefix and the generator found them by it; when they lost the prefix it found none,
// and nothing failed.
public class LifecycleFlagEmissionTests
{
    private const string Entries = "RaskBuilderEntryHost.g.cs";

    [Theory]
    [InlineData("OnMount")]
    [InlineData("OnUpdated")]
    [InlineData("OnFirstRendered")]
    [InlineData("OnRendered")]
    [InlineData("OnUnmount")]
    public void A_component_that_overrides_a_lifecycle_hook_is_entered_as_having_a_lifecycle(string hook)
    {
        var entries = EntriesFor($$"""
                                   public partial class Loader : Component
                                   {
                                       protected override async Task {{hook}}() { }
                                   }
                                   """);

        Assert.DoesNotContain(", false", EntryOf(entries, "Loader"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_component_with_no_hooks_is_entered_as_having_nothing_to_run()
    {
        var entries = EntriesFor("public partial class Quiet : Component { public string? Note { get; set; } }");

        Assert.Contains(", false", EntryOf(entries, "Quiet"), StringComparison.Ordinal);
    }

    // Entries are hung on the components that consume them, so the sample needs one besides the subject.
    private static string EntriesFor(string component) =>
        BuilderGeneratorHarness.Run($$"""
                                      using System.Threading.Tasks;
                                      using Rask.Core;
                                      namespace Demo;
                                      {{component}}
                                      public partial class Page : Component { }
                                      """).Source(Entries);

    // The generated member for one component, from its name to the end of its statement.
    private static string EntryOf(string entries, string component)
    {
        var start = entries.IndexOf($" {component} =>", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No entry for {component} in:\n{entries}");
        return entries[start..entries.IndexOf(';', start)];
    }
}
