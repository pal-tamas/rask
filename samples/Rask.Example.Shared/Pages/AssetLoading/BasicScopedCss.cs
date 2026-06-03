namespace Rask.Example.Shared.Pages.AssetLoading;

/// <summary>
///     Single component with a sibling <c>BasicScopedCss.css</c>. When this component is
///     mounted, the framework emits one
///     <c>&lt;link href="/_rask/a/{hash}.css" data-rask-key="rsk-css-{hash}"&gt;</c>
///     into <c>&lt;head&gt;</c>. The browser fetches the bytes from the endpoint with
///     <c>Cache-Control: immutable</c>.
/// </summary>
public sealed class BasicScopedCss : Component
{
    protected override RenderResult Render() =>
        Div(Class: "basic-card")[
            P()["This card's pink background and rounded corners come from a sibling ",
                Code()["BasicScopedCss.css"],
                " file. The framework hashes the rewritten CSS and emits a ",
                Code()["<link>"],
                " into the page head — open DevTools and you should see a request to ",
                Code()["/_rask/a/{12-hex}.css"],
                " with ",
                Code()["cache-control: public, max-age=31536000, immutable"],
                "."
            ]
        ];
}
