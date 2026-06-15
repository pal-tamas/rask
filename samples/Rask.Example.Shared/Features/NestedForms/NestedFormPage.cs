using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("nested-forms")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NestedFormPage : Component
{
    protected override RenderResult Head => Title()["Complex models — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Complex models",
            "Forms aren't always flat. Drop a single DataAnnotationsValidator() or FluentValidationValidator(...) at the top of the Form and the same Input(Bind: ...) syntax binds through sub-objects, lists, and dictionaries. The reference-based FieldIdentifier means each sub-instance owns its own validation state, so add/remove/reorder works without re-keying."),
        H2(Class: "h4 mt-4 mb-3")["Sub-object — () => model.Address.Street"],
        CodeSample(
            EmbeddedSource.Read("NestedSubObjectDemo.cs"),
            Notes:
            "The graph walker in DataAnnotationsValidator visits the Address sub-object automatically, so [Required] / [RegularExpression] on its properties fire just like the root model's. ValidationMessage(() => _model.Address.Street, ...) reads the message off the Address instance — not off the root — so reassigning _model.Address = new(...) between renders rewires the bindings without leftover errors.",
            Result: NestedSubObjectDemo()),
        H2(Class: "h4 mt-5 mb-3")["Collection — foreach + per-item capture"],
        CodeSample(
            EmbeddedSource.Read("NestedListForeachDemo.cs"),
            Notes:
            "Each foreach iteration closes over a different `item` reference, so the resulting lambdas point at distinct instances — each row owns its own validation state. When the user removes a row, that item's FieldIdentifier entries simply stop being read; no key juggling. When they add a row, the new item starts with empty state.",
            Result: NestedListForeachDemo()),
        H2(Class: "h4 mt-5 mb-3")["Collection — indexer with the for-loop closure workaround"],
        CodeSample(
            EmbeddedSource.Read("NestedListIndexerDemo.cs"),
            Notes:
            "Indexer binding compiles to a MethodCallExpression on get_Item (List<T>) or BinaryExpression(ArrayIndex) (T[]). The parser invokes it each render, so reassigning _model.Skus[i] (record replacement, reorder) is picked up next frame without any rebind boilerplate. The catch is the C# `for` closure trap — copy the index into a per-iteration local. `foreach` doesn't have this problem.",
            Result: NestedListIndexerDemo()),
        H2(Class: "h4 mt-5 mb-3")["FluentValidation — SetValidator and RuleForEach"],
        CodeSample(
            EmbeddedSource.Read("NestedFluentValidationDemo.cs"),
            Notes:
            "FluentValidation already walks nested rules via .SetValidator(...) and RuleForEach(...). Rask's job is just to route the dotted PropertyName (\"Address.Street\", \"Lines[0].Description\") back to the runtime sub-instance so the message lands where ValidationMessage(For: () => _model.Address.Street, ...) is reading. Per-keystroke validation on a root-model field uses MemberNameValidatorSelector (fast path); on a nested field it runs the full validator and filters by resolved owner.",
            Result: NestedFluentValidationDemo())
    ];
}
