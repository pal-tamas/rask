using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>A field with a rule and a checkbox, neither with a timing step, in a form that says what it saved.</summary>
internal sealed partial class DeferredFieldStubApp : Component
{
    private readonly Draft _draft = new();
    private string _saved = "";

    protected override Component? HeadAssets => Title["deferred field"];

    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        Form.Model(_draft).OnSubmit(draft => _saved = $"{draft.Name}/{draft.Agreed}")[
            Input.Bind(() => _draft.Name)
                .Validate(name => name.Length < 3 ? ["Name is too short."] : []),
            Validation.Message.Template(messages => P[messages[0]]).For(() => _draft.Name),
            Input.Bind(() => _draft.Agreed),
            P[$"name={_draft.Name}|saved={_saved}"]
        ];

    private sealed class Draft
    {
        public string Name { get; set; } = "";

        public bool Agreed { get; set; }
    }
}
