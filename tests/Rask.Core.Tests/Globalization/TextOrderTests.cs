using System.Globalization;

namespace Rask.Core.Tests.Globalization;

[Collection(CultureCollection.Name)]
public sealed class TextOrderTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void Hungarian_puts_cs_after_cu_o_umlaut_after_every_o_and_accented_a_with_a()
    {
        string[] names = ["Zoltán", "Ödön", "csaba", "Cukor", "Ottó", "Ábel", "Adorján", "ábrahám", "Dénes"];

        var ordered = names.Order(TextOrder.In("hu")).ToArray();

        Assert.Equal(["Ábel", "ábrahám", "Adorján", "Cukor", "csaba", "Dénes", "Ottó", "Ödön", "Zoltán"], ordered);
    }

    [Fact]
    public void An_unaccented_a_comes_before_its_accented_twin_and_case_changes_nothing()
    {
        var order = TextOrder.In("hu");

        var accent = order.Compare("a", "á");
        var letterCase = order.Compare("Csaba", "csaba");

        Assert.True(accent < 0);
        Assert.Equal(0, letterCase);
    }

    [Fact]
    public void The_current_order_is_the_visitor_s_culture_s()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");

        var order = TextOrder.Current;

        Assert.Same(TextOrder.In("hu-HU"), order);
        Assert.True(order.Compare("Cukor", "Csaba") < 0);
    }
}
