using Rask.Core;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A form as an app writes a refund: a required name, a radio that says which fields come beneath it, and
///     rules of its own on the year, the quarter and the day those fields ask for.
/// </summary>
public sealed partial class KindFormApp : Component
{
    private readonly Refund _model = new();

    protected override Component? HeadAssets => Markup.Title["kind-form"];

    protected override Component? Render() =>
    [
        Main[
            Form.Model(_model).OnSubmit(Save).ConfirmLeave("Leave?")[
                Ui.Field[Ui.Label["Name"], Ui.Input.Bind(() => _model.Name).ShowValidation(false), Ui.Error],
                Ui.RadioGroup.Bind(() => _model.Kind).Live().Label("Kind")[
                    Enum.GetValues<Kinds>().Select(kind => Ui.Radio.Key(kind.ToString()).Value((Kinds?)kind).Label(kind.ToString()))
                ],
                Fields(),
                Ui.Button.Primary.Submit.Id("save")["Save"]
            ]
        ],
        Ui.Toast,
    ];

    private Component? Fields() => _model.Kind switch
    {
        Kinds.Periodic => Div.Key("days")[
            Ui.Field.Key("from")[Ui.Label["From"], Ui.DatePicker.Bind(() => _model.From).Validate(day => day == default ? ["Required"] : []), Ui.Error]
        ],
        _ => Div.Key("calendar")[
            Ui.Field.Key("year")[Ui.Label["Year"], Calendar(() => _model.Year), Ui.Error],
            _model.Kind == Kinds.Quarterly
                ? Ui.Field.Key("quarter")[Ui.Label["Quarter"], Calendar(() => _model.Quarter), Ui.Error]
                : null
        ],
    };

    private Component Calendar(System.Linq.Expressions.Expression<Func<int?>> field) =>
        Ui.Select.Bind(field).Listbox.Searchable(true).Placeholder("Choose").ShowValidation(false)
            .Validate(value => value is null ? ["Required"] : [])[
            Enumerable.Range(2020, 5).Select(year => Ui.SelectOption.Key("o" + year).Value((int?)year)[year.ToString()])];

    private static Task Save(Refund model) => Task.CompletedTask;

    public enum Kinds
    {
        Yearly,
        Quarterly,
        Periodic,
    }

    public sealed class Refund
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string? Name { get; set; }

        public Kinds? Kind { get; set; } = Kinds.Yearly;

        public int? Year { get; set; }

        public int? Quarter { get; set; }

        public DateOnly? From { get; set; }
    }
}
