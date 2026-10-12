#pragma warning disable RASK022 // rows nothing keys are what most of these cases are about

namespace Rask.Core.Tests.Live;

/// <summary>
///     Thirty-six changes that put one node in the PLACE of another, each drawn inside the same page: a menu
///     of sixty links, a header with a search field, and the content that changes. They are the shapes pages
///     are written in — <c>cond ? A : B</c>, words that become an element, a placeholder that becomes a list,
///     one page in the outlet giving way to the next.
/// </summary>
internal sealed partial class RefusedLevelCorpus : global::Rask.Core.RaskMarkup
{
    internal const string Conditional = "cond ? A : B";
    internal const string TextAndElement = "text <-> element";
    internal const string LoadingToList = "loading -> list";
    internal const string PageSwap = "page swap";
    internal const string Compound = "compound";
    internal const string Other = "other";

    /// <summary>Each case by name: the kind of change it is, and the content drawn before and after it.</summary>
    internal static readonly IReadOnlyDictionary<string, (string Category, Func<bool, Component> Draw)> Cases =
        new Dictionary<string, (string, Func<bool, Component>)>(StringComparer.Ordinal)
        {
            ["a status that is a span or an em"] = (Conditional, on => Card(on ? Em["Shipped"] : Span["Open"])),
            ["a button that becomes a link"] = (Conditional, on => Card(on ? A.Href("/orders/7")["Open"] : Button["Open"])),
            ["a view that becomes its edit form"] = (Conditional, on => Card(on
                ? Fieldset[Input.Value("Ada").Name("name"), Button["Save"]]
                : Div[Span["Ada"], Button["Edit"]])),
            ["a paragraph that becomes a list"] = (Conditional, on => Card(on ? Ul[Li["one"], Li["two"]] : P["one, two"])),
            ["an input that becomes a textarea"] = (Conditional, on => Card(on ? Textarea.Value("").Name("note") : Input.Value("").Name("note"))),
            ["a heading that changes level"] = (Conditional, on => Card(on ? H3["Order 7"] : H2["Order 7"])),
            ["a picture that becomes a drawing"] = (Conditional, on => Card(on ? Svg[Circle.R("9")] : Img.Src("/ada.png").Alt("Ada"))),
            ["details that become a plain block"] = (Conditional, on => Card(on ? Div["Two days."] : Details[Summary["Shipping"], "Two days."])),
            ["a section that becomes an article in a keyed row"] = (Conditional, on => Ul[
                Li.Key("a")[Section["Alpha"]], Li.Key("b")[on ? Article["Beta"] : Section["Beta"]]
            ]),
            ["a table that becomes the words for none"] = (Conditional, on => Card(on ? P["No orders yet."] : Rows(3))),

            ["words that gain a span around them"] = (TextAndElement, on => Card(Button[on ? Span["Save"] : "Save"])),
            ["words that become a list"] = (TextAndElement, on => Card(on ? Ul[Li["a"]] : "Loading")),
            ["words that become bold"] = (TextAndElement, on => Card(P[on ? B["3 open"] : "3 open"])),
            ["a span that becomes its words"] = (TextAndElement, on => Card(P[on ? "Ada" : Span["Ada"]])),
            ["words between two elements that become one"] = (TextAndElement, on => Card(P[B["Ada"], on ? Em["and"] : " and ", B["Grace"]])),
            ["initials that become an icon"] = (TextAndElement, on => Card(Span.Class("avatar")[on ? Svg[Circle.R("9")] : "AL"])),
            ["words that become a link"] = (TextAndElement, on => Card(P[on ? A.Href("/orders/7")["Order 7"] : "Order 7"])),
            ["an em that becomes words before a link"] = (TextAndElement, on => Card(P[on ? "now" : Em["now"], A.Href("/orders/7")["shipped"]])),
            ["words that become a field"] = (TextAndElement, on => Card(Label[Span["Name"], on ? Input.Value("Ada").Name("name") : "Ada"])),
            ["words in a cell that become a block"] = (TextAndElement, on => Card(Table[Tbody[Tr[Td["Ada"], Td[on ? Div["Paid"] : "Paid"]]]])),

            ["a loading line that becomes a list"] = (LoadingToList, on => Card(on ? Ul[Items(15)] : P["Loading"])),
            ["a spinner that becomes a table"] = (LoadingToList, on => Card(on ? Rows(15) : Div.Class("spinner"))),
            ["a loading span that becomes a numbered list"] = (LoadingToList, on => Card(on ? Ol[Items(15)] : Span["Loading"])),
            ["a loading line that becomes a grid of cards"] = (LoadingToList, on => Card(on
                ? Div.Class("grid")[Enumerable.Range(1, 15).Select(n => (Component)Article[H3["Order " + n], P["Open"]])]
                : P["Loading"])),
            ["a skeleton list that becomes a table"] = (LoadingToList, on => Card(on ? Rows(15) : Ul.Class("skeleton")[Li, Li, Li])),
            ["a loading line that becomes a description list"] = (LoadingToList, on => Card(on
                ? Dl[Enumerable.Range(1, 15).SelectMany(n => new Component[] { Dt["Order " + n], Dd["Open"] })]
                : P["Loading"])),
            ["a loading line that becomes a keyed list above a pager"] = (LoadingToList, on => Div.Class("card")[
                H2["Orders"],
                on ? Ul[Enumerable.Range(1, 15).Select(n => (Component)Li.Key(n)["Order " + n])] : P["Loading"],
                Nav.Class("pager")[A.Href("?page=2")["Next"]]
            ]),

            ["the list page giving way to the form page"] = (PageSwap, on => Div.Id("outlet")[on ? FormPage() : ListPage()]),
            ["the form page giving way to the list page"] = (PageSwap, on => Div.Id("outlet")[on ? ListPage() : FormPage()]),
            ["a page of one node giving way to a page of three"] = (PageSwap, on => Div.Id("outlet")[
                on ? ListPage() : [Aside.Id("missing")["No such order."]]
            ]),
            ["two pages that start alike and then differ"] = (PageSwap, on => Div.Id("outlet")[
                on
                    ? [H1["Order 7"], Dl[Dt["Total"], Dd["120"]], Button["Edit"]]
                    : [H1["Dashboard"], Div.Class("grid")[Article["Open: 3"], Article["Late: 1"]], P["As of today."]]
            ]),

            ["two children arriving apart from each other"] = (Compound, on => Div.Class("card")[
                on ? Aside["Saved"] : null, H2["Orders"], on ? P["Nothing else to do."] : null, Ul[Li["a"]]
            ]),
            ["a child arriving while a sibling changes element"] = (Compound, on => Div.Class("card")[
                H2["Orders"], on ? Aside["Saved"] : null, on ? Em["a"] : Span["a"], Ul[Li["a"]]
            ]),
            ["a replacement at two depths at once"] = (Compound, on => Div.Class("card")[
                on ? Em["Shipped"] : Span["Open"], Div.Class("body")[H3["Lines"], on ? Ul[Li["a"]] : P["Loading"]]
            ]),

            ["raw markup that changes, alone in its parent"] = (Other, on => Card(Div.Class("prose")[
                Raw.Value(on ? "<p>Two <b>days</b>.</p><p>Free.</p>" : "<p>Three days.</p>")
            ])),
            ["a shape in a drawing that becomes another"] = (Other, on => Card(Svg[G[Circle.R("5")], on ? Circle.R("9") : G[Circle.R("9")]])),
        };

    /// <summary>The page every case is drawn in.</summary>
    internal static Component Page(Component content) =>
        Body[
            Nav.Class("sidebar")[Enumerable.Range(1, 60).Select(n => (Component)A.Href("/menu/" + n)["Menu item " + n])],
            Header[H1["Orders"], Input.Value("").Name("search")],
            Main[content]
        ];

    private static Component Card(Component changing) =>
        Div.Class("card")[H2["Orders"], Div.Class("slot")[changing], Footer["3 in all"]];

    private static IEnumerable<Component> Items(int count) => Enumerable.Range(1, count).Select(n => (Component)Li["Order " + n]);

    private static Component Rows(int count) =>
        Table[Tbody[Enumerable.Range(1, count).Select(n => (Component)Tr[Td["Order " + n], Td["Open"], Td[Button["Edit"]]])]];

    private static Component[] ListPage() => [H1["Orders"], Rows(15), Nav.Class("pager")[A.Href("?page=2")["Next"]]];

    private static Component[] FormPage() =>
    [
        H1["New order"],
        Fieldset[Label["Name", Input.Value("").Name("name")], Label["Note", Textarea.Value("").Name("note")], Button["Save"]],
    ];
}
