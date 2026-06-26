namespace Rask.Example.Shared.Features;

// Every text-level / inline element, live. Each is a generator-emitted factory in
// Rask.Core.Components.Generated; children go through the [...] indexer.
public sealed class ElementsTextDemo : Component
{
    protected override RenderResult Render() => Div(Class: "vstack gap-2")[
        P()[
            "Link ", A(Href: "https://example.com", Target: "_blank", Rel: "noopener")["an anchor"],
            ", ", Strong()["strong"], ", ", B()["bold"], ", ", Em()["emphasis"], ", ", I()["idiomatic"],
            ", ", U()["underline"], ", ", S()["struck"], ", ", Small()["small"], ", ", Mark()["highlight"],
            ", and ", Span(Class: "text-accent")["a plain span"], "."
        ],
        P()[
            "Inline code ", Code()["Div()[…]"], ", a key ", Kbd()["Ctrl"], "+", Kbd()["C"],
            ", sample output ", Samp()["exit 0"], ", a variable x", Sub()["1"], " to the n", Sup()["2"], "."
        ],
        P()[
            "Define a term: ", Dfn()["Rask"], " is a C# UI framework. Abbreviate it ", Abbr()["UI"],
            ", cite ", Cite()["The Pragmatic Programmer"], ", quote ", Q(Cite: "https://example.com")["inline quote"],
            ", machine-readable ", Data(Value: "42")["forty-two"], ", and a ", Time(DateTime: "2026-06-26")["date"], "."
        ],
        // Bidirectional + ruby annotations.
        P()[
            "Isolated user text ", Bdi()["إعلان"], "; overridden direction ", Bdo(Dir: "rtl")["this is RTL"], ". ",
            Ruby()["漢", Rp()["("], Rt()["kan"], Rp()[")"]], " annotates pronunciation."
        ],
        // A long word with a soft break opportunity, and a line break.
        P(Class: "mb-0")[
            "Super", Wbr(), "cali", Wbr(), "fragilistic.", Br(), "Edits: ",
            Ins(Cite: "https://example.com", DateTime: "2026-06-26")["added"], " and ",
            Del(DateTime: "2026-06-25")["removed"], "."
        ]
    ];
}
