using Rask.Core.Live;

namespace Rask.Example.Playground;

// Root of the playground WASM app. Renders the full document shell (RASK021); the single-page UI,
// editor, compile orchestration and live preview all live in PlaygroundView. Public + non-sealed to match
// the host's ActivatorUtilities.CreateInstance + DAM contract.
public class PlaygroundApp : Component
{
    protected override Component? Head =>
    [
        Title()["Rask Playground"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
        Link(Rel: "icon", Type: "image/svg+xml", Href: LiveOptions.PathBase + "/icon.svg"),
        Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/global.css")
    ];

    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            Body()[
                PlaygroundView()
            ]
        ]
    ];
}
