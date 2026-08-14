using static Rask.Core.Tests.Generated;

namespace Rask.Core.Tests;

// PROTOTYPE — how a user's OWN components become entries without a base class.
//
// Framework tags ride on Component itself (Rask.Core declares that class, so it can carry them).
// A user's components can't: a generator in the consumer's project cannot add members to
// Rask.Core.Component, and delivering entries via `using static` does not work — a static-imported
// property loses to a same-named type in scope (CS0119). Injecting them into the consuming
// component's own `partial` sidesteps both: a member of the enclosing type wins outright.
//
// The cost is that entries are per-class rather than shared, so the generator emits one forwarder
// per user component per consuming component.
internal sealed partial class Chip : Component
{
    public new string? Text { get; set; }

    protected override Component? Render() => Strong[Text ?? ""];
}

// The page derives from Component — nothing else.
internal sealed partial class ChipHost : Component
{
    protected override Component? Render() => Div[Chip.Text("new")];
}

// The generator injects `private static Chip Chip => Entry<Chip>();` into this partial.
internal sealed partial class ChipHost
{
    internal static Type Probe() => typeof(Chip);
}

public partial class BuilderPartialEntryTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_user_component_is_an_entry_without_a_base_class() =>
        Assert.Equal("<div><strong>new</strong></div>", ChipHost.ToHtml());

    // The entry DOES shadow the type's static members now, because it hands back `Build<ChipHost>`
    // rather than a `ChipHost` — C#'s "Color Color" rule only merges the two when the property's type
    // IS the type. `Chip` still names a type, so the fix is to say which one is meant.
    [Fact]
    public void The_type_stays_usable_alongside_its_entry() =>
        Assert.Equal(typeof(Chip), global::Rask.Core.Tests.ChipHost.Probe());
}
