using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Example.Shared.Features;

namespace Rask.Example.Shared;

// The Rails-guides-style chrome around a single guide's prose. Given a slug it reads docs/{slug}.md and
// lays it out like rubyonrails.org/docs: a slim version/source banner, a numbered "Chapters" table of
// contents built from the guide's headings, the guide body (Markdown, which mounts any inline demos),
// a sticky "On this page" rail that scroll-spies the current section, and prev/next book-navigation
// following the GuideCatalog order. The scroll-spy runs entirely on the client (GuideChrome.js) — no
// server round-trips — so it costs nothing on either transport.
public sealed class GuideChrome : Component
{
    private readonly IJSRuntime _js;

    // A ref to the whole guide root so the scoped JS can scope its heading/anchor queries to this
    // component's subtree (and tear the observer down when the guide unmounts on SPA nav).
    private readonly ElementRef _root = ElementRef.New();

    // Slug is a required factory param (non-nullable, no initializer) assigned by Rask after
    // construction, so the CS8618s on the ctor and the property are expected — same shape as CodeSample.
#pragma warning disable CS8618
    public GuideChrome(IJSRuntime js) => _js = js;

    // Required positional param: the guide slug, e.g. "routing".
    public string Slug { get; set; }
#pragma warning restore CS8618

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        // Wire the scroll-spy once the guide body is in the DOM. Guarded because the guide can render a
        // not-found state (no headings) and because JS may be unavailable on a torn-down transport.
        if (!firstRender)
        {
            return;
        }

        try
        {
            await _js.InvokeVoidAsync("Rask.GuideChrome.spy", _root);
        }
        catch (JSDisconnectedException)
        {
            // The circuit went away before the guide finished mounting — nothing to spy on.
        }
    }

    protected override async Task OnUnmountAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("Rask.GuideChrome.stop", _root);
        }
        catch (JSDisconnectedException)
        {
            // Teardown during disconnect — the observer dies with the page anyway.
        }
    }

    protected override Component? Render()
    {
        var source = GuideCatalog.ReadMarkdown(Slug);
        if (source is null)
        {
            return
            [
                BackLink(),
                BsAlert(Color: BsColor.Warning)[$"No guide found for “{Slug}”."]
            ];
        }

        var headings = Markdown.Headings(source);
        var (prev, next) = Adjacent(Slug);

        return Div(Ref: _root, Class: "guide")[
            Banner(),
            BackLink(),
            Chapters(headings),
            Div(Class: "guide-layout")[
                Div(Class: "guide-content")[Markdown(source)],
                OnThisPage(headings)
            ],
            PrevNext(prev, next)
        ];
    }

    private static Component BackLink() =>
        NavLink(Href: Features.Routes.GuidesIndexPage(), ActiveClass: "",
            Class: Bs.Join(Display.InlineFlex(), Flex.Align(BsAlign.Center), Margin.Bottom(3),
                Txt.DecorationNone, "small", "guide-backlink"))[
            BsIcon(Name: BsIconName.ArrowLeft, Class: "me-1"), "All guides"
        ];

    private Component Banner() =>
        Div(Class: "guide-banner")[
            BsIcon(Name: BsIconName.InfoCircle, Class: "me-2"),
            Span()[$"You're reading the Rask v{RaskVersion.Current} guides."],
            A(Href: $"https://github.com/pal-tamas/rask/blob/main/docs/{Slug}.md", Target: "_blank",
                Rel: "noopener", Class: "guide-banner-src")[
                BsIcon(Name: BsIconName.Github, Class: "me-1"), "View source"
            ]
        ];

    // The numbered Chapters TOC: each ## is a chapter; the ### under it become a nested sub-list. Mirrors
    // the "Chapters" box at the top of every Rails guide.
    private static Component? Chapters(IReadOnlyList<Markdown.Heading> headings)
    {
        if (headings.Count == 0)
        {
            return null;
        }

        var chapters = new List<Component>();
        for (var i = 0; i < headings.Count; i++)
        {
            if (headings[i].Level != 2)
            {
                continue;
            }

            var subs = new List<Component>();
            for (var j = i + 1; j < headings.Count && headings[j].Level == 3; j++)
            {
                subs.Add(Li(Key: headings[j].Id)[Anchor(headings[j], "guide-chapter-sublink")]);
            }

            chapters.Add(Li(Key: headings[i].Id)[
                subs.Count == 0
                    ? Anchor(headings[i], "guide-chapter-link")
                    : [
                        Anchor(headings[i], "guide-chapter-link"),
                        Ol(Class: "guide-chapters-sub")[subs]
                    ]
            ]);
        }

        return Nav(Class: "guide-chapters", Aria: new Dictionary<string, string?> { ["label"] = "Chapters" })[
            Div(Class: "guide-chapters-title")["Chapters"],
            Ol(Class: "guide-chapters-list")[chapters]
        ];
    }

    // The sticky secondary rail; the client scroll-spy toggles .active on the link whose section is in
    // view. data-spy carries the target id so the JS can match without parsing the href.
    private static Component? OnThisPage(IReadOnlyList<Markdown.Heading> headings)
    {
        if (headings.Count == 0)
        {
            return null;
        }

        return Aside(Class: "guide-onthispage")[
            Nav(Class: "guide-onthispage-inner",
                Aria: new Dictionary<string, string?> { ["label"] = "On this page" })[
                Div(Class: "guide-onthispage-title")["On this page"],
                Ul(Class: "guide-onthispage-list")[
                    headings.Select(h => (Component)Li(
                        Key: h.Id,
                        Class: h.Level == 3 ? "guide-onthispage-sub" : null)[
                        Anchor(h, "guide-onthispage-link")
                    ])
                ]
            ]
        ];
    }

    // A fragment anchor into the guide body; the scroll-spy matches rail links by their "#id" href.
    private static Component Anchor(Markdown.Heading h, string cssClass) =>
        A(Href: $"#{h.Id}", Class: cssClass)[h.Text];

    private static Component? PrevNext(GuideEntry? prev, GuideEntry? next)
    {
        if (prev is null && next is null)
        {
            return null;
        }

        return Nav(Class: "guide-prevnext",
            Aria: new Dictionary<string, string?> { ["label"] = "Guide navigation" })[
            prev is null
                ? Span(Class: "guide-prevnext-spacer")
                : NavLink(Href: Features.Routes.GuidePage(prev.Slug), ActiveClass: "",
                    Class: "guide-prevnext-link guide-prevnext-prev")[
                    BsIcon(Name: BsIconName.ArrowLeft, Class: "me-2"),
                    Span(Class: "guide-prevnext-body")[
                        Span(Class: "guide-prevnext-label")["Previous"],
                        Span(Class: "guide-prevnext-title")[prev.Title]
                    ]
                ],
            next is null
                ? Span(Class: "guide-prevnext-spacer")
                : NavLink(Href: Features.Routes.GuidePage(next.Slug), ActiveClass: "",
                    Class: "guide-prevnext-link guide-prevnext-next")[
                    Span(Class: "guide-prevnext-body")[
                        Span(Class: "guide-prevnext-label")["Next"],
                        Span(Class: "guide-prevnext-title")[next.Title]
                    ],
                    BsIcon(Name: BsIconName.ArrowRight, Class: "ms-2")
                ]
        ];
    }

    // The previous/next guide in reading order: GuideCatalog.All flattened by GroupOrder. Kept here (not
    // in GuideCatalog) so the ordering logic sits with the chrome that uses it, and stays unit-testable.
    internal static (GuideEntry? Prev, GuideEntry? Next) Adjacent(string slug)
    {
        var ordered = ReadingOrder();
        var index = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Slug == slug)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return (null, null);
        }

        return (index > 0 ? ordered[index - 1] : null,
            index < ordered.Count - 1 ? ordered[index + 1] : null);
    }

    // The guides in reading order: by GroupOrder, then original catalog order within a group. A guide in
    // no known group sorts after the known groups but keeps its relative order.
    internal static IReadOnlyList<GuideEntry> ReadingOrder()
    {
        var order = GuideCatalog.GroupOrder;
        return GuideCatalog.All
            .Select((entry, i) => (entry, i))
            .OrderBy(t =>
            {
                var g = Array.IndexOf(order, t.entry.Group);
                return g < 0 ? order.Length : g;
            })
            .ThenBy(t => t.i)
            .Select(t => t.entry)
            .ToArray();
    }
}
