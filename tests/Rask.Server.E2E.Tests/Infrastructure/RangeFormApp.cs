using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The form an app reported, as that app writes it: a routed page under a layout that places the toasts and
///     the leave dialog, a multiple listbox bound to a list of ids between other bound fields, a native select
///     after them, and a save that needs every one.
/// </summary>
public sealed partial class RangeFormApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["ranges"], Markup.Base.Href("/uj/")];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() => Router;
}

[Route("/ranges")]
public sealed partial class RangeLayout : Component
{
    protected override Component? Render() =>
    [
        Header[Nav[NavLink.Href(Routes.RangeListPage()).Id("list")["Lőterek"]]],
        Main[Outlet],
        Footer[P.Id("ready")["ready"]],
        Ui.Toast,
        Ui.ConfirmLeave.Stay("Nem").Leave("Igen"),
    ];
}

[Route("")]
[ParentRoute(typeof(RangeLayout))]
public sealed partial class RangeListPage : Component
{
    protected override Component? Render() => P.Id("ranges")["Nincs lőtér"];
}

/// <summary>
///     The same form on a page whose first pick changes an unkeyed child's element, which the diff gate refuses:
///     that one reply is the whole page, as every pick's was in the app that reported.
/// </summary>
[Route("new-whole")]
[ParentRoute(typeof(RangeLayout))]
public sealed partial class RangeFormWholePagePickPage : RangeFormPage
{
    protected override Component? Summary(int picked) => picked > 0 ? Div.Id("summary")["Van pisztoly"] : Span.Id("summary")["Nincs pisztoly"];
}

/// <summary>The same form with the listbox named, so it posts one hidden field a picked pistol.</summary>
[Route("new-named")]
[ParentRoute(typeof(RangeLayout))]
public sealed partial class RangeFormNamedPage : RangeFormPage
{
    protected override string? PostedAs => "pistols";
}

[Route("new")]
[ParentRoute(typeof(RangeLayout))]
public partial class RangeFormPage : Component
{
    /// <summary>The name the listbox posts its picks under. None here, as in the app that reported.</summary>
    protected virtual string? PostedAs => null;

    /// <summary>What the page says about the picks, between the form and what was saved. Nothing here.</summary>
    protected virtual Component? Summary(int picked) => null;

    private const string Required = "Kötelező kitölteni";

    private static readonly (string Id, string Name)[] Pistols =
    [
        ("7f1c", "Glock"), ("7f2c", "Beretta"), ("7f3c", "Walther"), ("7f4c", "Sig Sauer"), ("7f5c", "Colt"),
        ("7f6c", "Ruger"), ("7f7c", "Smith & Wesson"), ("7f8c", "Heckler & Koch"), ("7f9c", "CZ"),
        ("7fac", "Springfield"), ("7fbc", "Taurus"), ("7fcc", "Kimber"), ("7fdc", "Steyr"),
    ];

    private static readonly string[] Kinds = ["Fedett", "Nyitott"];

    private readonly Draft _form = new();
    private string _saved = string.Empty;

    protected override Component? Render() =>
    [
        H1["Új lőtér"],
        Form.Model(_form).OnSubmit(Save).ConfirmLeave("Elveti a módosításokat?")[
            Ui.Field[
                Ui.Label.Badge("Kötelező")["Megnevezés"],
                Ui.Input.Bind(() => _form.Name).ShowValidation(false).Validate(name => name.Length > 0 ? [] : [Required]),
                Ui.Error
            ],
            Ui.Field[
                Ui.Label["Pisztolyok"],
                Ui.Select.Bind(() => _form.PistolIds).Listbox.Multiple().Placeholder("Válasszon…").Name(PostedAs)[
                    Pistols.Select(pistol => Ui.SelectOption.Key(pistol.Id).Value(pistol.Id)[pistol.Name])
                ],
                Ui.Error
            ],
            Ui.Field[
                Ui.Label.Badge("Kötelező")["Méret"],
                Ui.Input.Bind(() => _form.Size).Type(InputType.Number).ShowValidation(false).Validate(size => size is >= 1 ? [] : [Required]),
                Ui.Error
            ],
            Ui.Field[
                Ui.Label.Badge("Kötelező")["Pályák"],
                Ui.Input.Bind(() => _form.Lanes).Type(InputType.Number).ShowValidation(false).Validate(lanes => lanes is >= 1 ? [] : [Required]),
                Ui.Error
            ],
            Ui.Field[
                Ui.Label["Megjegyzés"],
                Ui.Input.Bind(() => _form.Note),
                Ui.Error
            ],
            Ui.Field[
                Ui.Label["Típus"],
                Ui.Select.Bind(() => _form.Kind).Placeholder("Válasszon…")[
                    Kinds.Select(kind => Ui.SelectOption.Key(kind).Value(kind)[kind])
                ],
                Ui.Error
            ],
            Ui.Button.Primary.Submit.Id("save")["Mentés"]
        ],
        Summary(_form.PistolIds.Count),
        P.Id("saved")[_saved],
    ];

    private void Save() =>
        _saved = $"{_form.Name}|{string.Join(',', _form.PistolIds)}|{_form.Size}|{_form.Lanes}|{_form.Note}|{_form.Kind}";

    private sealed class Draft
    {
        public string Name { get; set; } = string.Empty;

        public List<string> PistolIds { get; set; } = [];

        public int? Size { get; set; }

        public int? Lanes { get; set; }

        public string Note { get; set; } = string.Empty;

        public string? Kind { get; set; }
    }
}
