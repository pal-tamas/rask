using Rask.Site.E2E.Tests.Infrastructure;

// ONE static host over the published site and ONE browser for the whole assembly, handed to every test
// class's constructor. Each test still opens a browser context of its own (SharedSmokeTests, and every
// class that calls NewContextAsync), so no cookie, storage, permission, route or page leaks from one test
// into another.
//
// Assembly fixtures rather than a collection fixture is what lets the classes run side by side: a
// collection fixture puts every class that shares it into ONE collection, and xUnit runs a collection's
// tests one at a time — this suite ran 220-odd journeys serially that way with maxParallelThreads at 4.
// Now each class is a collection of its own and xunit.runner.json's thread count applies.
[assembly: AssemblyFixture(typeof(WasmExampleAppFixture))]
[assembly: AssemblyFixture(typeof(PlaywrightFixture))]

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     The classes that read back what they put on the clipboard. Headless Chromium has one clipboard for the
///     whole browser, not one per context, so two of them running at once could each read the other's text:
///     they share this collection and run one after another. gates.yml deals them to different shards too,
///     where each has the browser to itself.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ClipboardCollection
{
    public const string Name = "Clipboard";
}
