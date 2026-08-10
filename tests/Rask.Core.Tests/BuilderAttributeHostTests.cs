using System.Reflection;

namespace Rask.Core.Tests;

// PROTOTYPE — the two shapes `: RaskMarkup` cannot reach, and the attribute that does.
//
// Deriving from RaskMarkup is the cheap way onto the builder surface and stays the default: the 166
// framework entries arrive by ordinary inheritance, one link, no generated members. It spends the base
// slot to do it — which is fine right up until the slot is not yours to spend.
//
// [RaskMarkup] says the same thing without one. It is not a second mechanism: when the attributed type's
// base slot is still free the generator writes `: RaskMarkup` into the type's own generated partial, so
// the delivery is identical and so is the cost. Only when the slot is genuinely unavailable does it fall
// back to injecting the entries as members, which is the expensive form and is why it is the fallback.

// Shape one: the base belongs to someone else. xUnit's TheoryData<…> is a base a test cannot give up
// and cannot edit — and a data source that builds the components its theory asserts on is exactly the
// code that wants the surface. Nothing here could have been written with `: RaskMarkup`.
[RaskMarkup]
public partial class ChipCases : TheoryData<Func<Component>, string>
{
    public ChipCases()
    {
        Add(() => Div.Class("wrap")[Em["new"]], "<div class=\"wrap\"><em>new</em></div>");
        Add(() => Strong["hi"], "<strong>hi</strong>");
        Add(() => Ul[Li["a"], Li["b"]], "<ul><li>a</li><li>b</li></ul>");
    }
}

// Shape two: a `static class`, which can derive from nothing at all. This is DemoRegistry's shape —
// markup built inside lambdas held in a static lookup — and it stays static.
//
// `Map` is what makes it interesting. The <map> tag's entry wants that name too, and here the entry
// would be a SECOND member of this very type rather than an inherited one, so `new` cannot rescue it
// (CS0102). The generator leaves every name the type already reaches alone instead; the member below is
// the one that keeps it.
[RaskMarkup]
public static partial class AttributeDemoRegistry
{
    private static readonly Dictionary<string, Func<Component>> Map = new(StringComparer.Ordinal)
    {
        ["badge"] = () => Div.Class("wrap")[Em["new"]],
        ["title"] = () => H1["Demos"],
    };

    public static Component Build(string key) => Map[key]();

    public static IEnumerable<string> Keys => Map.Keys;
}

public class BuilderAttributeHostTests
{
    [Theory]
    [ClassData(typeof(ChipCases))]
    public void A_host_whose_base_is_not_ours_still_names_markup(Func<Component> build, string expected) =>
        Assert.Equal(expected, build().ToHtml());

    [Fact]
    public void A_static_class_names_markup_without_giving_up_being_static()
    {
        Assert.Equal("<div class=\"wrap\"><em>new</em></div>", AttributeDemoRegistry.Build("badge").ToHtml());
        Assert.Equal("<h1>Demos</h1>", AttributeDemoRegistry.Build("title").ToHtml());
    }

    // The member that shares a tag's name is still the author's, and still means what it meant.
    [Fact]
    public void A_member_named_after_a_tag_keeps_the_name() =>
        Assert.Equal(["badge", "title"], AttributeDemoRegistry.Keys);

    // A static class cannot be instantiated — which is the whole of what being static buys, and the
    // reason it was worth getting back. `IsAbstract && IsSealed` is what `static` compiles to.
    [Fact]
    public void The_static_class_is_still_static() =>
        Assert.True(typeof(AttributeDemoRegistry) is { IsAbstract: true, IsSealed: true });

    // Opting in is a declaration, so it stays with the declaration. A subclass of an attributed host is
    // not itself a host: nothing is injected into it, it needs no `partial`, and a one-line edit to a
    // shared base cannot turn into RASK036 in files that name no markup. (It still reaches the entries,
    // because they are its base's members — inheritance does that for free. What it does not get is a
    // second copy of them, and the `partial` that copy would have demanded.)
    private sealed class NotAHost : ChipCases;

    [Fact]
    public void A_subclass_of_an_attributed_host_is_not_one()
    {
        const BindingFlags own = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(ChipCases).GetProperty("Div", own));
        Assert.Null(typeof(NotAHost).GetProperty("Div", own));
    }
}
