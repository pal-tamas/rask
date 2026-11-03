namespace Rask.Site.Features.UiDocs.Examples;

// expect: shows a field in the dialog, and Yes waits until the answer is typed exactly, case and all.
public sealed partial class ConfirmExpectExample : Component
{
    private readonly List<string> _partners = ["Acme", "Globex", "Initech"];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ul.Class("max-w-sm divide-y divide-zinc-800/10 dark:divide-white/20")[
                _partners.Select(name => Li.Key(name).Class("flex items-center gap-2 py-2")[
                    Ui.Text.Class("grow")[name],
                    Ui.Button.Sm.Danger.Id($"uc-wipe-{name}")
                        .OnClick(() => _partners.Remove(name))
                        .Confirm($"Type {name} to delete it.", expect: name, heading: "Delete partner?")["Delete"]
                ])
            ],
            Ui.Text.Id("uc-left")[$"{_partners.Count} partners"]
        ];
}
