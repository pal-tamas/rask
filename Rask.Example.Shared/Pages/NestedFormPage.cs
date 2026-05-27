using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

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
                """
                public sealed class CheckoutModel {
                    [Required, StringLength(60)] public string Name { get; set; } = "";
                    [Required, EmailAddress]    public string Email { get; set; } = "";
                    public AddressModel Address { get; set; } = new();
                }

                public sealed class AddressModel {
                    [Required]                                  public string Street { get; set; } = "";
                    [Required]                                  public string City   { get; set; } = "";
                    [Required, RegularExpression("^[A-Z]{2}$",
                        ErrorMessage = "Use the ISO 2-letter code.")]
                                                                public string Country { get; set; } = "";
                }

                Form<CheckoutModel>(_model, OnValidSubmit: m => ...)[
                    DataAnnotationsValidator(),
                    Input(() => _model.Name),
                    Input(() => _model.Email),
                    // Sub-object fields — same Bind syntax, no extra validator declaration:
                    Input(() => _model.Address.Street),
                    Input(() => _model.Address.City),
                    Input(() => _model.Address.Country)
                ]
                """,
                Notes:
                "The graph walker in DataAnnotationsValidator visits the Address sub-object automatically, so [Required] / [RegularExpression] on its properties fire just like the root model's. ValidationMessage(() => _model.Address.Street, ...) reads the message off the Address instance — not off the root — so reassigning _model.Address = new(...) between renders rewires the bindings without leftover errors.",
                Result: NestedSubObjectDemo()),
            H2(Class: "h4 mt-5 mb-3")["Collection — foreach + per-item capture"],
            CodeSample(
                """
                public sealed class LineItem {
                    [Required, StringLength(80)] public string Description { get; set; } = "";
                    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
                    public int Quantity { get; set; } = 1;
                }

                // Inside Render:
                List<Child> rows = new();
                foreach (var item in _model.Items) {
                    rows.Add(Tr()[
                        Td()[Input(() => item.Description, Class: "form-control")],
                        Td()[Input(() => item.Quantity,    Class: "form-control")],
                        Td()[Button(OnClick: () => _model.Items.Remove(item))["×"]]
                    ]);
                }
                """,
                Notes:
                "Each foreach iteration closes over a different `item` reference, so the resulting lambdas point at distinct instances — each row owns its own validation state. When the user removes a row, that item's FieldIdentifier entries simply stop being read; no key juggling. When they add a row, the new item starts with empty state.",
                Result: NestedListForeachDemo()),
            H2(Class: "h4 mt-5 mb-3")["Collection — indexer with the for-loop closure workaround"],
            CodeSample(
                """
                // for (int i = …) captures `i` by reference — without the local copy, every
                // lambda would close over the loop's final value. Standard C# closure trap.
                for (var idx = 0; idx < _model.Skus.Count; idx++) {
                    var i = idx;                       // <-- per-iteration capture
                    rows.Add(Tr()[
                        Td(Class: "text-secondary small")[$"#{i + 1}"],
                        Td()[Input(() => _model.Skus[i].Code,  Class: "form-control")],
                        Td()[Input(() => _model.Skus[i].Price, Class: "form-control")]
                    ]);
                }
                """,
                Notes:
                "Indexer binding compiles to a MethodCallExpression on get_Item (List<T>) or BinaryExpression(ArrayIndex) (T[]). The parser invokes it each render, so reassigning _model.Skus[i] (record replacement, reorder) is picked up next frame without any rebind boilerplate. The catch is the C# `for` closure trap — copy the index into a per-iteration local. `foreach` doesn't have this problem.",
                Result: NestedListIndexerDemo()),
            H2(Class: "h4 mt-5 mb-3")["FluentValidation — SetValidator and RuleForEach"],
            CodeSample(
                """
                public sealed class OrderValidator : AbstractValidator<OrderModel> {
                    public OrderValidator() {
                        RuleFor(x => x.CustomerName).NotEmpty();
                        // Sub-object — declare a separate validator and SetValidator into it.
                        RuleFor(x => x.Address).SetValidator(new AddressValidator());
                        // Collection — RuleForEach + SetValidator per item.
                        RuleForEach(x => x.Lines).SetValidator(new LineValidator());
                    }
                }

                Form<OrderModel>(_model, OnValidSubmit: m => ...)[
                    FluentValidationValidator(new OrderValidator()),
                    Input(() => _model.CustomerName),
                    Input(() => _model.Address.Street),     // Address.Street routes to AddressValidator
                    // foreach line in _model.Lines: Input(() => line.Description)  → LineValidator
                ]
                """,
                Notes:
                "FluentValidation already walks nested rules via .SetValidator(...) and RuleForEach(...). Rask's job is just to route the dotted PropertyName (\"Address.Street\", \"Lines[0].Description\") back to the runtime sub-instance so the message lands where ValidationMessage(For: () => _model.Address.Street, ...) is reading. Per-keystroke validation on a root-model field uses MemberNameValidatorSelector (fast path); on a nested field it runs the full validator and filters by resolved owner.",
                Result: NestedFluentValidationDemo())
        ];
}
