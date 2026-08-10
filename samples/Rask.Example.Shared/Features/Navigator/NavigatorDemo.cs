using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// Navigator is a scoped service injected through the ctor. It mutates the route only from
// event-handler code — a button click here changes the path, a select changes just the query.
public sealed partial class NavigatorDemo(Navigator nav) : Component
{
    protected override Component? Render() =>
        BsStack.Vertical(true).Gap(2)[
            Button
                .OnClick(() => nav.NavigateTo("/dashboard"))["Open dashboard"],

            // Or update just the query, keeping the same path:
            Select<string>()
                .OnChange(v => nav.SetQuery("sort", v))[
                Option.Value("asc")["Sort ascending"],
                Option.Value("desc")["Sort descending"]
            ]
        ];
}
