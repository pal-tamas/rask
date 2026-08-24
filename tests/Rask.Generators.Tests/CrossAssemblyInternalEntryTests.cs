using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

/// <summary>
///     What a friend assembly is told about a referenced library's INTERNAL components. An internal
///     component publishes an <c>internal static</c> entry on its assembly's <c>RaskEntries{Assembly}</c>
///     class, so a scan that takes public members only hands a friend assembly nothing — even though it
///     can see both the component and the entry. With the factory gone there is no second spelling left,
///     and the test has to name the entry host in full to reach the component at all.
/// </summary>
public class CrossAssemblyInternalEntryTests
{
    private const string Library = """
        namespace Lib
        {
            public sealed class OpenCard : global::Rask.Core.Component
            {
                protected override global::Rask.Core.Component? Render() => null;
            }

            internal sealed class SecretCard : global::Rask.Core.Component
            {
                protected override global::Rask.Core.Component? Render() => null;
            }
        }
        """;

    private const string Consumer = """
        using Rask.Core;

        namespace Demo
        {
            public partial class Page : Component
            {
            }
        }
        """;

    private const string OpenEntry =
        "private static global::Rask.Core.Build<global::Lib.OpenCard> OpenCard "
        + "=> global::RaskEntriesLib.OpenCard;";

    private const string SecretEntry =
        "private static global::Rask.Core.Build<global::Lib.SecretCard> SecretCard "
        + "=> global::RaskEntriesLib.SecretCard;";

    [Fact]
    public void A_referenced_librarys_internal_entry_is_injected_into_a_friend_assembly()
    {
        var entries = ConsumerEntries(friend: "TestAssembly");

        Assert.Contains(SecretEntry, entries, StringComparison.Ordinal);
        Assert.Contains(OpenEntry, entries, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The negative control that gives the one above its meaning: same library, same internal
    ///     component, an <c>InternalsVisibleTo</c> naming somebody else. The public entry still arrives,
    ///     so the scan ran — the internal one is withheld because the grant does not reach here, not
    ///     because nothing was scanned.
    /// </summary>
    [Fact]
    public void Without_the_grant_the_internal_entry_is_withheld_and_the_public_one_is_not()
    {
        var entries = ConsumerEntries(friend: "SomebodyElse");

        Assert.DoesNotContain(SecretEntry, entries, StringComparison.Ordinal);
        Assert.Contains(OpenEntry, entries, StringComparison.Ordinal);
    }

    /// <summary>
    ///     …and the entry the friend assembly reaches is the library's own canonical one, emitted
    ///     <c>internal static</c> because the component is internal. This pins the shape the scan reads,
    ///     so a change on the emitting side cannot quietly make the fix above a no-op.
    /// </summary>
    [Fact]
    public void The_library_publishes_the_internal_entry_as_an_internal_member()
    {
        var host = BuilderGeneratorHarness.Run(Library).Source("RaskBuilderEntryHost.g.cs");

        Assert.Contains(
            "internal static global::Rask.Core.Build<global::Lib.SecretCard> SecretCard",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Rask.Core.Build<global::Lib.OpenCard> OpenCard",
            host,
            StringComparison.Ordinal);
    }

    // Emits the library — generated entry host included — and runs the generator over a consumer that
    // references it. Emitting is what makes this a real test of the scan: it reads metadata, and an
    // internal member's accessibility is exactly the thing that survives the round trip.
    private static string ConsumerEntries(string friend)
    {
        var library = BuilderGeneratorHarness.Compile(
            $"""
             [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("{friend}")]

             """ + Library,
            "Lib");

        using var stream = new MemoryStream();
        var emit = library.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        stream.Position = 0;

        return BuilderGeneratorHarness
            .Run(Consumer, new[] { MetadataReference.CreateFromStream(stream) })
            .Source("RaskBuilderConsumerEntries.g.cs");
    }
}
