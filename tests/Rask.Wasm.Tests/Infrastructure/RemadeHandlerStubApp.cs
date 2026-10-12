using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>
///     A page whose every render makes its handlers again — they close over what that render worked out — while
///     the page it draws stays the same: what a tagless element handed children does on every walk.
/// </summary>
internal sealed partial class RemadeHandlerStubApp : Component
{
    private int _renders;
    private int _shown;

    /// <summary>What the handlers saw, for a test to read without the page showing it.</summary>
    public static List<int> Heard { get; } = [];

    protected override Component? HeadAssets => Markup.Title["remade"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        // New in every render, and on no part of the page.
        var render = ++_renders;
        return
        [
            Markup.P[$"shown={_shown};"],
            Button.Id("quiet").OnClick(() => Heard.Add(render))["quiet"],
            Button.Id("loud").OnClick(() => { Heard.Add(render); _shown++; })["loud"]
        ];
    }
}
