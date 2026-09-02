namespace Rask.Example.Shared;

// The title + lead block every showcase page opens with. A component rather than a static helper: it
// returns markup and nothing else, which is the framework's own idiom (it is what Div.Class(Tw.Card) is), and only a
// component can reach the builder surface — entries are inherited members, so a static class sees none
// of them.
internal sealed partial class PageHeader : Component
{
    // `new` because `Title` is also the <title> tag's builder entry, inherited from Component. That is
    // the collision the entry surface creates and the one its CS0108 quick-fix resolves: hiding an entry
    // inside your own class is your decision, and this component has no use for a <title>.
    public required new string Title { get; set; }

    public required string Lead { get; set; }

    protected override Component? Render() =>
        Div.Class("mb-4 pb-3 border-b")[
            H1.Class("text-3xl font-bold mb-2")[Title],
            P.Class("text-lg text-slate-500 dark:text-slate-400 mb-0")[Lead]
        ];
}
