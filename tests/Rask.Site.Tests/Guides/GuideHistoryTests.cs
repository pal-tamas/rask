using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

/// <summary>
///     A guide's "last changed" date is git's, per file — or absent. Never a guess.
/// </summary>
public sealed class GuideHistoryTests
{
    [Fact]
    public void TheNewestDateWinsWhateverOrderTheLogListsItIn()
    {
        // A branch merged late lists commits dated BEFORE ones already printed. "First seen" would date cqrs
        // to September 1st; its newest change was the 10th.
        const string Log = """
            @2026-09-01

            docs/cqrs.md
            @2026-09-10

            docs/apis/geolocation.md
            docs/cqrs.md
            @2026-08-01

            docs/cqrs.md
            """;

        var dates = GuideHistory.Parse(Log);

        Assert.Equal(new DateOnly(2026, 9, 10), dates["cqrs"]);

        // Keyed by the bare leaf, because that is the slug: docs/apis/geolocation.md is /docs/guides/geolocation.
        Assert.Equal(new DateOnly(2026, 9, 10), dates["geolocation"]);
    }

    [Fact]
    public void WhatIsNotADatedDocIsIgnored()
    {
        // A path before any date line, a date git did not print, a file outside docs/, a file that is not
        // Markdown, and the error text of a build with no repository — which the target captures too.
        const string Log = """
            docs/orphan.md
            @not-a-date
            docs/undated.md
            @2026-01-02
            src/Rask.Core/README.md
            docs/data/seed.json
            fatal: not a git repository (or any of the parent directories): .git
            """;

        Assert.Empty(GuideHistory.Parse(Log));
    }

    [Fact]
    public void AnEmptyHistoryDatesNothing() =>
        // What a shallow clone, or a build with no git, embeds.
        Assert.Empty(GuideHistory.Parse(string.Empty));

    [Fact]
    public void TheBuildEmbeddedAHistoryThatDatesTheGuides()
    {
        // The seam, not the parser: the csproj target ran, found the repository, and embedded what git said.
        // Every part of this can fail silently — a wrong -C path, a shallow clone, a resource name that does
        // not match — and each looks identical from the page: no date. These tests run from a full checkout,
        // where a guide that has existed since the docs did must have one.
        var dated = GuideCatalog.All.Count(guide => GuideHistory.LastModified(guide.Slug) is not null);

        Assert.True(
            dated == GuideCatalog.All.Length,
            $"only {dated} of {GuideCatalog.All.Length} guides have a git date. Either the RaskSiteGuideHistory "
            + "target did not run, this is a shallow clone, or a guide's file is not committed yet.");
    }
}
