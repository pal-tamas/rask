namespace Rask.Example.Shared.Features;

// Document & metadata elements — html, head, body, title, base, link, meta, style, script, noscript —
// build the page shell, so they can't render live *inside* this page. Instead the demo composes a real
// shell and shows its serialized output via ToHtml(). template/slot (inert/shadow-DOM) render below.
public sealed partial class ElementsMetadataDemo : Component
{
    // Illustrative only: a real app declares head content by overriding the `Head` property (which is
    // why RASK019 normally flags Head()[…] children) — this composes the elements directly just to show
    // them and their serialized output, so the analyzer is suppressed here on purpose.
#pragma warning disable RASK019
    private static Component Shell() => Html.Lang("en").Dir("ltr")[
        Head[
            Meta.Charset("utf-8"),
            Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
            Title["My page"],
            Base.Href("/"),
            Link.Rel("stylesheet").Href("/app.css"),
            Style["body{margin:0}"],
            Script.Src("/app.js").Defer(true),
            Noscript["This app needs JavaScript."]
        ],
        Body[P["Hello world"]]
    ];
#pragma warning restore RASK019

    protected override Component? Render() => Div.Class("flex flex-col gap-3")[
        Div[
            P.Class("text-sm mb-1 text-ui-muted")[
                "The structural elements compose a document. Here is a real shell and its serialized HTML:"],
            Pre.Class("bg-slate-900 text-slate-100 rounded p-3 mb-0").Style("white-space:pre-wrap;word-break:break-word")[
                Code[Shell().ToHtml()]]
        ],
        Div[
            P.Class("text-sm mb-1 text-ui-muted")[
                "template holds inert content (cloned by JS); slot is a shadow-DOM placeholder:"],
            Div.Class("border rounded p-2")[
                Template.Id("row-tmpl")[Li["Inert template content"]],
                Slot.Name("label")["Default slot content"],
                P.Class("mb-0 mt-1 text-ui-muted text-sm")[
                    "(the ", Code["template"], " content is hidden by the browser until cloned)"]
            ]
        ]
    ];
}
