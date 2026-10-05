
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
            Select.Of<string>()
                .OnChange(v => Go.With("sort", v))[
                Option.Value("asc")["Sort ascending"],
                Option.Value("desc")["Sort descending"]
            ]
        ];
}
