using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wire;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The first change of a control that shows and hides other fields, after a save was refused, in a real
///     browser over a store that answers each commit: diffs answer it, and the page never follows them.
/// </summary>
/// <remarks>
///     A store's answer arms the form's own timer, which asks for one more render a moment later. That render
///     drew nothing new, but wrote the invalid field's two ARIA attributes in the other order, and the page was
///     sent for it, onto whatever the reader had typed since.
/// </remarks>
public sealed class KindChosenAfterRefusedSaveTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // The form's timer asks for its render 200 ms after the store has answered.
    private const int TimerAndAFewFramesMs = 1000;

    [Theory]
    [InlineData("Quarterly", "Quarter")]
    [InlineData("Periodic", "From")]
    public async Task The_first_kind_chosen_after_a_refused_save_is_answered_with_diffs_and_no_page(string kind, string appears)
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<KindFormPage>(
            playwright, environment: "Development", beforeLoad: page =>
            {
                frames = SocketFrames.Of(page);
                return Task.CompletedTask;
            });
        var page = session.Page;
        await page.ClickAsync("#save");
        await Expect(page.Locator("#name-error")).ToBeVisibleAsync();
        frames!.Clear();

        await page.Locator("label", new() { HasTextString = kind }).First.ClickAsync();
        await Expect(page.GetByText(appears, new() { Exact = true })).ToBeVisibleAsync();
        await Task.Delay(TimerAndAFewFramesMs, TestContext.Current.CancellationToken);

        Assert.Empty(frames.FullPages);
        Assert.NotEmpty(frames.Replies());
    }
}

/// <summary>A unique index the store keeps, which holds for every name: it answers a moment after each commit.</summary>
internal sealed class FreeStore : IStoreRules
{
    internal static readonly FreeStore Instance = new();

    public async ValueTask<IReadOnlyList<FieldFailure>> Check(object model, string? field, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken);

        return [];
    }
}

/// <summary>A required name, a kind chosen as it is clicked, and the fields that kind asks for beneath it.</summary>
public sealed partial class KindFormPage : Component
{
    private readonly Refund _model = new();

    static KindFormPage() => RaskValidation.RegisterStoreRules(typeof(Refund), _ => FreeStore.Instance);

    protected override Component? HeadAssets => Markup.Title["kind-form"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Main[
            Form.Model(_model).OnSubmit(Save)[
                Ui.Field[Ui.Label["Name"], Ui.Input.Bind(() => _model.Name).ShowValidation(false), Ui.Error.Id("name-error")],
                Ui.RadioGroup.Bind(() => _model.Kind).Live().Label("Kind")[
                    Enum.GetValues<Kinds>().Select(kind => Ui.Radio.Key(kind.ToString()).Value((Kinds?)kind).Label(kind.ToString()))
                ],
                Fields(),
                Ui.Button.Primary.Submit.Id("save")["Save"]
            ]
        ];

    private Component Fields() => _model.Kind == Kinds.Periodic
        ? Div.Key("days")[Ui.Field.Key("from")[Ui.Label["From"], Ui.DatePicker.Bind(() => _model.From), Ui.Error]]
        : Div.Key("calendar")[
            Ui.Field.Key("year")[Ui.Label["Year"], Ui.Input.Bind(() => _model.Year).ShowValidation(false), Ui.Error],
            _model.Kind == Kinds.Quarterly
                ? Ui.Field.Key("quarter")[Ui.Label["Quarter"], Ui.Input.Bind(() => _model.Quarter).ShowValidation(false), Ui.Error]
                : null
        ];

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
