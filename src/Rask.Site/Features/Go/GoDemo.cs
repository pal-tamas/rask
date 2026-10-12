
namespace Rask.Site.Features;

// Go moves the user from event-handler code, with nothing injected — a button click here changes the
// path (Go.To), a select changes just the query (Go.With).
public sealed partial class GoDemo : Component
{
    protected override Component? Render() =>
        Div.Class("flex gap-2 flex-col")[
            Button
                .OnClick(() => Go.To("/dashboard"))["Open dashboard"],

            // Or update just the query, keeping the same path:
            Ui.Select.Of<string>().Listbox.Placeholder("Sort…")
                .OnChange(v => Go.With("sort", v))[
                Ui.SelectOption.Value("asc")["Sort ascending"],
                Ui.SelectOption.Value("desc")["Sort descending"]
            ]
        ];
}
