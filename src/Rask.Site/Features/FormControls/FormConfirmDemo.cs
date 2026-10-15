namespace Rask.Site.Features;

// Asking before an action: `.Confirm("…")` shows the browser's own confirm before the click leaves the page, and
// `.Confirm("…", expect: name)` asks for the name to be typed. Cancelled, nothing is sent.
public sealed partial class FormConfirmDemo : Component
{
    private readonly List<string> _partners = ["Acme", "Globex", "Initech"];

    protected override Component? Render() =>
        Div.Id("fc-demo")[
            Ul.Class("mb-2")[
                _partners.Select(name => Li.Key(name).Class("flex items-center gap-2 py-1")[
                    Span.Class("grow")[name],
                    Ui.Button.Icon(Ui.IconName.Trash).Id($"fc-delete-{name}")
                        .OnClick(() => _partners.Remove(name)).Confirm($"Delete {name}?")["Delete"],
                    Ui.Button.Danger.Id($"fc-wipe-{name}")
                        .OnClick(() => _partners.Remove(name)).Confirm($"Type {name} to delete it", expect: name)["Delete by name"]
                ])
            ],
            P.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").Id("fc-left")[$"{_partners.Count} partners"]
        ];
}
