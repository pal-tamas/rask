using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

/// <summary>
///     Who keeps a simple name when a REFERENCED library publishes an entry under it too — here the real
///     Rask.Dashboard, every harness compilation's reference, and its public <c>NotFoundPage</c>. The nearer
///     component wins: the compilation's own over any referenced library's, and a library over one it
///     reaches through its own references — so a test project that references an app (which reaches the
///     dashboard through Rask.Server) still sees the app's <c>NotFoundPage</c>. RASK040 is left for a clash
///     nothing orders: two components of one compilation, or two libraries that do not reach each other.
/// </summary>
public class ReferencedEntryPrecedenceTests
{
    private const string Entries = "RaskBuilderConsumerEntries.g.cs";

    private const string DashboardHost = "global::RaskEntriesRask_Dashboard.NotFoundPage";

    // An app the way a RaskApp is one: it names Rask.Server, which is what carries Rask.Dashboard.
    private const string AppSource = """
        namespace App
        {
            public sealed partial class NotFoundPage : global::Rask.Core.Component
            {
                public static System.Type Host => typeof(global::Rask.RaskApp);

                protected override global::Rask.Core.Component? Render() => null;
            }
        }
        """;

    // The same page in a library that does not reach the dashboard at all. Not partial, so nothing is
    // injected into it: an injected forwarder onto a dashboard entry would itself be a reference to the
    // dashboard — the library would have SEEN it, and its own page already beat the dashboard's there.
    private const string UnrelatedSource = """
        namespace Other
        {
            public sealed class NotFoundPage : global::Rask.Core.Component
            {
                protected override global::Rask.Core.Component? Render() => null;
            }
        }
        """;

    private const string TestProjectSource = """
        using Rask.Core;
        namespace App.Tests
        {
            public partial class Probe : Component { }
        }
        """;

    [Fact]
    public void An_apps_own_component_keeps_the_entry_over_a_referenced_librarys_of_the_same_name()
    {
        var source = """
            using Rask.Core;
            namespace App
            {
                public partial class NotFoundPage : Component { }
                public partial class Page : Component { }
            }
            """;

        var run = BuilderGeneratorHarness.Run(source);

        Assert.Empty(run.WithId("RASK040"));
        var entries = run.Source(Entries);
        Assert.Contains(
            "private static global::App.NotFoundPage NotFoundPage => global::RaskEntriesTestAssembly.NotFoundPage;",
            entries, StringComparison.Ordinal);
        Assert.DoesNotContain(DashboardHost, entries, StringComparison.Ordinal);
    }

    [Fact]
    public void A_test_project_sees_the_apps_component_over_the_library_the_app_reaches()
    {
        var app = Emit(AppSource, "App");

        var run = BuilderGeneratorHarness.Run(TestProjectSource, [app]);

        Assert.Empty(run.WithId("RASK040"));
        var entries = run.Source(Entries);
        Assert.Contains(
            "private static global::App.NotFoundPage NotFoundPage => global::RaskEntriesApp.NotFoundPage;",
            entries, StringComparison.Ordinal);
        Assert.DoesNotContain(DashboardHost, entries, StringComparison.Ordinal);
    }

    [Fact]
    public void The_librarys_component_the_app_shadows_stays_reachable_qualified()
    {
        var app = Emit(AppSource, "App");
        var source = """
            using Rask.Core;
            namespace App.Tests
            {
                public partial class Probe : Component
                {
                    public static global::Rask.Dashboard.Pages.NotFoundPage Theirs() => global::RaskEntriesRask_Dashboard.NotFoundPage;
                    public static global::App.NotFoundPage Ours() => NotFoundPage;
                }
            }
            """;

        var compilation = BuilderGeneratorHarness.Compile(source, references: [app]);

        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(errors);
    }

    [Fact]
    public void Two_libraries_that_do_not_reach_each_other_still_collide_as_RASK040()
    {
        var other = Emit(UnrelatedSource, "Other");

        var run = BuilderGeneratorHarness.Run(TestProjectSource, [other]);

        var collision = Assert.Single(run.WithId("RASK040"));
        Assert.Contains("global::Other.NotFoundPage", collision.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("global::Rask.Dashboard.Pages.NotFoundPage", collision.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(" NotFoundPage =>", run.Source(Entries), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_own_components_of_one_name_are_RASK040_and_the_librarys_does_not_take_the_name()
    {
        var source = """
            using Rask.Core;
            namespace App.Shop
            {
                public partial class NotFoundPage : Component { }
            }
            namespace App.Admin
            {
                public partial class NotFoundPage : Component { }
            }
            """;

        var run = BuilderGeneratorHarness.Run(source);

        Assert.Equal(2, run.WithId("RASK040").Count());
        Assert.DoesNotContain(" NotFoundPage =>", run.Source(Entries), StringComparison.Ordinal);
    }

    // Emits a library — its generated entry host included — so the consumer's scan reads real metadata.
    private static MetadataReference Emit(string source, string name)
    {
        var compilation = BuilderGeneratorHarness.Compile(source, name);
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
