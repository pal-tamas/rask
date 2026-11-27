using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     A select bound to a member that may hold no value — <c>CompanyId?</c>, <c>int?</c>, <c>Guid?</c> — takes options of
///     the type it holds when it holds one: <c>Ui.SelectOption.Value(new CompanyId(3))</c>, as the docs say.
/// </summary>
public partial class UiSelectNullableOptionTests : global::Rask.Core.RaskMarkup
{
    private static readonly Guid Second = new("0190f0a4-0000-7000-8000-000000000002");

    private sealed class Contract
    {
        public UiTypedCompanyId? Company { get; set; } = new UiTypedCompanyId(2);

        public int? Count { get; set; } = 2;

        public Guid? Key { get; set; } = Second;
    }

    [Fact]
    public void A_select_of_a_nullable_typed_id_takes_options_of_the_typed_id()
    {
        var contract = new Contract();

        var page = Acting.Render(() => Ui.Select.Bind(() => contract.Company)[
            Ui.SelectOption.Value(new UiTypedCompanyId(1))["One"],
            Ui.SelectOption.Value(new UiTypedCompanyId(2))["Two"]
        ]);

        Assert.Equal("Two", page.TextOf("option[selected]"));
    }

    [Fact]
    public void An_option_of_another_type_is_refused_naming_the_types_the_page_wrote()
    {
        var contract = new Contract();

        var refused = Assert.ThrowsAny<Exception>(() => Acting.Render(() => Ui.Select.Bind(() => contract.Count).Listbox[
            Ui.SelectOption.Value(new UiTypedCompanyId(1))["One"]
        ]));

        Assert.Contains("holds UiTypedCompanyId, and its Ui.Select holds Int32?", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_listbox_of_a_nullable_typed_id_takes_options_of_the_typed_id()
    {
        var contract = new Contract();

        var page = Acting.Render(() => Ui.Select.Bind(() => contract.Company).Listbox[
            Ui.SelectOption.Value(new UiTypedCompanyId(1))["One"],
            Ui.SelectOption.Value(new UiTypedCompanyId(2))["Two"]
        ]);

        Assert.Equal("Two", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_select_of_a_nullable_number_takes_options_of_the_number()
    {
        var contract = new Contract();

        var page = Acting.Render(() => Ui.Select.Bind(() => contract.Count).Listbox[
            Ui.SelectOption.Value(1)["One"],
            Ui.SelectOption.Value(2)["Two"]
        ]);

        Assert.Equal("Two", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_select_of_a_nullable_guid_takes_options_of_the_guid()
    {
        var contract = new Contract();

        var page = Acting.Render(() => Ui.Select.Bind(() => contract.Key).Listbox[
            Ui.SelectOption.Value(Guid.Empty)["None"],
            Ui.SelectOption.Value(Second)["Second"]
        ]);

        Assert.Equal("Second", page.TextOf("[data-ui-select-button]"));
    }
}
