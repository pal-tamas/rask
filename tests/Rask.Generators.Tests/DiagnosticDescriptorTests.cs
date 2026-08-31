using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Tests;

/// <summary>
///     Invariants across the whole RASKxxx family rather than any one member of it, so a new diagnostic
///     inherits them by existing instead of by someone remembering.
/// </summary>
public class DiagnosticDescriptorTests
{
    // Every descriptor declared by every analyzer and generator in both analyzer assemblies. Read by
    // reflection because that is the only way to enumerate the ones generators hold in private statics —
    // and generator descriptors are exactly the half a per-analyzer check would miss.
    private static IReadOnlyList<DiagnosticDescriptor> AllDescriptors()
    {
        var found = new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal);

        foreach (var assembly in new[]
                 {
                     typeof(RoutesGenerator).Assembly,
                     typeof(Rask.Cqrs.Generators.CqrsDispatchGenerator).Assembly,
                 })
        {
            foreach (var type in assembly.GetTypes())
            {
                // Analyzers expose theirs through SupportedDiagnostics; generators keep them in private
                // static fields, which is where RASK001-013, 015-018, 020, 028, 029, 031 and 035 live.
                if (typeof(DiagnosticAnalyzer).IsAssignableFrom(type) && !type.IsAbstract
                    && Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
                {
                    Add(analyzer.SupportedDiagnostics);
                }

                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic
                                                     | BindingFlags.Public))
                {
                    if (field.GetValue(null) is DiagnosticDescriptor descriptor)
                    {
                        Add([descriptor]);
                    }
                }
            }
        }

        return found.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();

        void Add(ImmutableArray<DiagnosticDescriptor> descriptors)
        {
            foreach (var d in descriptors)
            {
                found[d.Id] = d;
            }
        }
    }

    private static IReadOnlyList<DiagnosticDescriptor> RaskDescriptors() =>
        AllDescriptors().Where(d => d.Id.StartsWith("RASK", StringComparison.Ordinal)).ToList();

    // The same enumeration WITHOUT collapsing by id, plus the type each descriptor was declared on.
    //
    // AllDescriptors above keys a dictionary on the id, which is what makes a collision invisible: two
    // different diagnostics numbered the same silently become one entry and every invariant here passes.
    // Deduplication is by the descriptor's OWN equality (DiagnosticDescriptor compares its fields), so one
    // descriptor reachable both through SupportedDiagnostics and through the static field behind it counts
    // once, while two genuinely different descriptors sharing an id stay two.
    private static IReadOnlyList<(string Owner, DiagnosticDescriptor Descriptor)> RaskDescriptorsByOwner()
    {
        var found = new List<(string, DiagnosticDescriptor)>();
        var seen = new HashSet<DiagnosticDescriptor>();

        foreach (var assembly in new[]
                 {
                     typeof(RoutesGenerator).Assembly,
                     typeof(Rask.Cqrs.Generators.CqrsDispatchGenerator).Assembly,
                 })
        {
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(DiagnosticAnalyzer).IsAssignableFrom(type) && !type.IsAbstract
                    && Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
                {
                    Add(type, analyzer.SupportedDiagnostics);
                }

                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic
                                                     | BindingFlags.Public))
                {
                    if (field.GetValue(null) is DiagnosticDescriptor descriptor)
                    {
                        Add(type, [descriptor]);
                    }
                }
            }
        }

        return found;

        void Add(Type owner, ImmutableArray<DiagnosticDescriptor> descriptors)
        {
            foreach (var d in descriptors)
            {
                if (d.Id.StartsWith("RASK", StringComparison.Ordinal) && seen.Add(d))
                {
                    found.Add((owner.Name, d));
                }
            }
        }
    }

    /// <summary>
    ///     One id, one diagnostic. Nothing else in the build enforces this, and it went wrong three times in
    ///     a single day.
    /// </summary>
    /// <remarks>
    ///     Two branches that each need a new diagnostic both read the highest number in
    ///     <c>docs/diagnostics.md</c> and both pick the next one. That file is documentation — the
    ///     descriptors are the source of truth — so nothing fails: the analyzers compile, both fire, and the
    ///     family ships two different meanings under one number, with one help link pointing at whichever
    ///     doc section was written second. RASK044/045 were already taken when a third branch wanted a
    ///     number, RASK046 had to be surrendered to Key-opens-the-chain, and RASK047 was claimed twice on
    ///     the same afternoon.
    /// </remarks>
    [Fact]
    public void No_two_diagnostics_share_an_id()
    {
        var collisions = RaskDescriptorsByOwner()
            .GroupBy(x => x.Descriptor.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} is declared {g.Count()} times: "
                         + string.Join(" | ", g.Select(x => $"{x.Owner} \"{x.Descriptor.Title}\"")))
            .ToList();

        Assert.True(collisions.Count == 0,
            "Two diagnostics cannot share an id — pick the next FREE number, and check the descriptors "
            + "rather than docs/diagnostics.md, which lags:\n  " + string.Join("\n  ", collisions));
    }

    [Fact]
    public void The_id_scan_sees_every_diagnostic()
    {
        // Same vacuity guard as The_family_is_discoverable_at_all, for the non-deduplicating enumeration:
        // if it found nothing, the collision check above would pass without ever comparing anything.
        Assert.True(RaskDescriptorsByOwner().Count >= 30,
            $"expected the whole RASK family, found {RaskDescriptorsByOwner().Count}");
    }

    [Fact]
    public void The_family_is_discoverable_at_all()
    {
        // Guards the reflection above: if it silently found nothing, every other test here would pass
        // vacuously and the invariants would stop being enforced without anything going red.
        Assert.True(RaskDescriptors().Count >= 30,
            $"expected the whole RASK family, found {RaskDescriptors().Count}");
    }

    /// <summary>
    ///     One family, one category. There used to be two — generators reported under
    ///     <c>Rask.Generators</c> and analyzers under <c>Usage</c> — which is an implementation detail of
    ///     this repo that leaked to the consumer: a category is what an <c>.editorconfig</c> rule or an
    ///     IDE's group-by keys on, so `dotnet_analyzer_diagnostic.category-Rask.severity = …` caught half
    ///     the family and silently ignored the rest (#609).
    /// </summary>
    [Fact]
    public void Every_diagnostic_reports_under_one_category()
    {
        var categories = RaskDescriptors()
            .Select(d => d.Category)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Rask"], categories);
    }

    /// <summary>
    ///     The one-line title is what an IDE shows in the squiggle; <c>Description</c> is the expanded
    ///     tooltip and the hover card. 26 of 35 left it null — including every build-breaking Error — so
    ///     the reader's only way to more detail was to click through to the docs, which is not something
    ///     you do mid-keystroke.
    /// </summary>
    [Fact]
    public void Every_diagnostic_carries_a_description()
    {
        var silent = RaskDescriptors()
            .Where(d => string.IsNullOrWhiteSpace(d.Description.ToString()))
            .Select(d => d.Id)
            .ToList();

        Assert.True(silent.Count == 0,
            "These diagnostics pass no `description:`, so the IDE's expanded tooltip shows nothing beyond "
            + "the one-line title:\n  " + string.Join("\n  ", silent));
    }

    [Fact]
    public void Every_diagnostic_links_to_its_documentation()
    {
        var unlinked = RaskDescriptors()
            .Where(d => string.IsNullOrWhiteSpace(d.HelpLinkUri)
                        || !d.HelpLinkUri.EndsWith(d.Id.ToLowerInvariant(), StringComparison.Ordinal))
            .Select(d => $"{d.Id} -> '{d.HelpLinkUri}'")
            .ToList();

        Assert.True(unlinked.Count == 0,
            "These diagnostics don't link to their own docs anchor:\n  " + string.Join("\n  ", unlinked));
    }
}
