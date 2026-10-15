using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/phone</c>, example by example.</summary>
/// <remarks>
///     <para>
///     The words and values are the rendered page's, which says more than its markdown does: every example
///     there has a label, a starting country and most a number (the invalid one holds <c>201</c>).
///     </para>
///     <para>
///     The page is measured AS LOADED and with no engine: Flux's field shows its number formatted by its script,
///     this one an empty input of the same box — the text inside an input is no node. What the engine does is
///     held by <c>tests/Rask.Server.E2E.Tests/UiPhoneTests</c>, and the open list by <c>parity-phone.mjs</c>.
///     </para>
/// </remarks>
public sealed partial class PhoneParity : FluxParity
{
    // The column Flux's docs page sets every phone example in.
    private const string Column = "display:flex;justify-content:center;max-width:384px;margin:auto";

    private static readonly string[] Four = ["US", "CA", "GB", "AU"];

    public override string Page => "phone";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", In(Ui.Phone.Value("").Country("US").Label("Phone number").Placeholder("(201) 555-0123")));

        yield return ("default-country", In(Ui.Phone.Value("+442071838750").Country("GB").Label("Office phone")));

        yield return ("country-list", In(Ui.Phone.Value("").Country("US").Countries(Four).Label("Support phone")));

        yield return ("country-order", In(Ui.Phone.Value("").Country("US").CountryOrder(Four).Label("Contact phone")));

        yield return ("filled", In(Ui.Phone.Value("+12015550123").Country("US").Filled.Label("Mobile phone")));

        yield return ("sizes", In(SpaceY(4,
            Ui.Phone.Value("+12015550123").Country("US").Sm,
            Ui.Phone.Value("+12015550123").Country("US").Xs)));

        yield return ("disabled-and-readonly", In(SpaceY(4,
            Ui.Phone.Value("+12015550123").Country("US").Disabled().Label("Disabled phone"),
            Ui.Phone.Value("+12015550123").Country("US").ReadOnly().Label("Readonly phone"))));

        yield return ("invalid", In(
            Ui.Phone.Value("201").Country("US").Invalid().Label("Phone number").Id("invalid-phone").DescriptionTrailing("Enter a complete phone number.")));
    }

    private static Component In(Component example) => Div.Style(Column)[Div.Style("width:100%;max-width:384px")[example]];
}
