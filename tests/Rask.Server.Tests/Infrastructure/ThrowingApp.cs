using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

public sealed partial class ThrowingApp : Component
{
    public int Counter;

    protected override Component? HeadAssets => new Title()["throw"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        new P()[$"count={Counter}"],
        Button.OnClick(() => throw new InvalidOperationException("boom"))["throw"],
        Button.OnClick(() => Counter++)["bump"]
    ];
}
