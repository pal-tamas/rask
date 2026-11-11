using System.ComponentModel.DataAnnotations;
using Rask.Wire;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

internal sealed class Shipment
{
    [Required]
    public string Reference { get; set; } = "";

    [Required]
    public string Carrier { get; set; } = "";
}

// A form whose save can refuse with a failure under a field, or throw something else.
internal sealed partial class ShipmentPage : Component
{
    internal readonly Shipment Model = new();
    internal Exception? Throws;

    protected override Component? Render() =>
        Form.Model(Model).OnSubmit(Save)[
            Input.Bind(() => Model.Reference).Id("reference"),
            Input.Bind(() => Model.Carrier).Id("carrier")
        ];

    private void Save(Shipment shipment)
    {
        if (Throws is not null)
        {
            throw Throws;
        }
    }
}

// What the server renders so the browser can tell a refused submit from an accepted one: `data-rask-refused`,
// the count of submits the form refused, moves with each refusal. The browser half — focus to the first invalid
// field — is FirstInvalidTests (Jint) and FirstInvalidFieldTests (a real browser).
public partial class FormRefusedMarkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_form_nobody_has_submitted_carries_no_refused_mark()
    {
        var page = Page.Render(new ShipmentPage());

        var mark = page.Attr("data-rask-refused");

        Assert.Null(mark);
    }

    [Fact]
    public async Task Each_submit_the_rules_refuse_moves_the_mark_and_marks_the_fields_invalid()
    {
        var page = Page.Render(new ShipmentPage());

        await page.On("form").Submit();
        var afterTheFirst = page.Attr("data-rask-refused");
        await page.On("form").Submit();

        Assert.Equal("1", afterTheFirst);
        Assert.Equal("2", page.Attr("data-rask-refused"));
        Assert.Equal("true", page.Find("#reference").Attribute("aria-invalid"));
        Assert.Equal("true", page.Find("#carrier").Attribute("aria-invalid"));
    }

    [Fact]
    public async Task An_accepted_submit_leaves_the_mark_as_it_was()
    {
        var shipment = new ShipmentPage();
        var page = Page.Render(shipment);
        await page.On("form").Submit();
        await page.On("#reference").Change("SH-1");
        await page.On("#carrier").Change("Posten");

        await page.On("form").Submit();

        Assert.Equal("1", page.Attr("data-rask-refused"));
        Assert.Null(page.Find("#reference").Attribute("aria-invalid"));
    }

    [Fact]
    public async Task A_save_that_refuses_with_a_field_failure_moves_the_mark_and_marks_that_field()
    {
        var shipment = new ShipmentPage { Throws = new RefusedException(new FieldFailure("That reference is taken.", ["Reference"])) };
        var page = Page.Render(shipment);
        await page.On("#reference").Change("SH-1");
        await page.On("#carrier").Change("Posten");

        await page.On("form").Submit();

        Assert.Equal("1", page.Attr("data-rask-refused"));
        Assert.Equal("true", page.Find("#reference").Attribute("aria-invalid"));
        Assert.Null(page.Find("#carrier").Attribute("aria-invalid"));
    }

    [Fact]
    public async Task A_save_that_throws_anything_else_is_no_refusal()
    {
        var shipment = new ShipmentPage { Throws = new InvalidOperationException("the store is down") };
        var page = Page.Render(shipment);
        await page.On("#reference").Change("SH-1");
        await page.On("#carrier").Change("Posten");

        await page.On("form").Submit();

        Assert.Null(page.Attr("data-rask-refused"));
    }


    private sealed class RefusedException(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }
}
