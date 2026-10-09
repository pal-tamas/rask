using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>One debounced field with a rule, and the message the rule leaves.</summary>
internal sealed partial class WaitingFieldStubApp : Component
{
    private readonly Draft _draft = new();

    protected override Component? HeadAssets => Title["waiting field"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Form.Model(_draft)[
            Input.Bind(() => _draft.Name).Debounce(300.Milliseconds)
                .Validate(name => name.Length < 3 ? ["Name is too short."] : []),
            Validation.Message.Template(messages => P[messages[0]]).For(() => _draft.Name),
            P[$"name={_draft.Name}"]
        ];

    private sealed class Draft
    {
        public string Name { get; set; } = "";
    }
}
