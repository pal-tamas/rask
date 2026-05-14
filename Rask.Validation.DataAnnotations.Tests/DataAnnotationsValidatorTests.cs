using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Forms;

#pragma warning disable RASK014 // test-only StubComponent subclass has no generated factory

namespace Rask.Validation.DataAnnotations.Tests;

public class DataAnnotationsValidatorTests
{
    [Fact]
    public async Task SubmitFlow_FirstInvalid_ThenFilled_RoutesToOnValidSubmit()
    {
        // Reproduces the showcase ValidationSummary demo flow as a unit test:
        //   1. Render Form (auto-attached via DataAnnotationsValidator child).
        //   2. Submit empty payload — must route to OnInvalidSubmit (which is null here,
        //      so neither typed handler fires; the bridge returns quietly).
        //   3. Re-render — the same EditContext must survive, the child re-registers
        //      idempotently, and the freshly-issued submit handler id must close over a
        //      context that still recognises the validator.
        //   4. Submit a valid payload via the new handler — must reach OnValidSubmit.
        var p = new Person { Name = "", Age = 0 };
        Person? captured = null;
        var view = new StubComponent(() => Form<Person>(
            p,
            (Action<Person>)(m => captured = m))[
                DataAnnotationsValidator(),
                Input(() => p.Name),
                Input(() => p.Age)
            ]);

        var html1 = view.RenderAsLiveRoot();
        var submit1 = ExtractAttr(html1, "data-rask-on-submit");
        Assert.NotNull(submit1);

        using (var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"\",\"Age\":\"0\"}}"))
        {
            await view.TryInvokeHandlerAsync(submit1!, doc.RootElement);
        }
        Assert.Null(captured);

        // Mutate the model out-of-band to simulate the user filling fields between renders
        // (the real Input bind events do this through NotifyFieldChanged; the second submit
        // is what we're stress-testing here, not the per-keystroke wiring).
        p.Name = "Ada";
        p.Age = 30;

        var html2 = view.RenderAsLiveRoot();
        var submit2 = ExtractAttr(html2, "data-rask-on-submit");
        Assert.NotNull(submit2);

        using (var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"Ada\",\"Age\":\"30\"}}"))
        {
            await view.TryInvokeHandlerAsync(submit2!, doc.RootElement);
        }
        Assert.Same(p, captured);
    }

    private static string? ExtractAttr(string html, string attr)
    {
        var marker = attr + "=\"";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var start = i + marker.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html.Substring(start, end - start);
    }

    private sealed class StubComponent : Component
    {
        private readonly Func<Component> _factory;
        public StubComponent(Func<Component> factory) => _factory = factory;
        protected override Component Render() => _factory();
    }

    [Fact]
    public void Validate_PopulatesMessages_PerOffendingMember()
    {
        var p = new Person { Name = "", Age = 0, Code = "" };
        var ctx = RegisterValidator(p);

        var ok = ctx.Validate();

        Assert.False(ok);
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
    }

    [Fact]
    public void Validate_AllValid_ReturnsTrue()
    {
        var p = new Person { Name = "Ada", Age = 30, Code = "ABC" };
        var ctx = RegisterValidator(p);
        Assert.True(ctx.Validate());
        Assert.False(ctx.HasValidationMessages());
    }

    [Fact]
    public void ValidateField_OnlyValidatesThatField()
    {
        var p = new Person { Name = "", Age = 999, Code = "" };
        var ctx = RegisterValidator(p);

        ctx.ValidateField(new FieldIdentifier(p, "Age"));

        Assert.NotEmpty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public void Component_Render_IsIdempotent_AcrossMultipleRenders()
    {
        var p = new Person { Name = "" };
        var ctx = new EditContext(p);

        // Two separate component instances each Render under the same context: AddValidator's
        // type-dedup should prevent double-registration. If duplicated, "Name is required"
        // would appear twice in the messages list.
        using (EditContextScope.Push(ctx))
        {
            DataAnnotationsValidator().ToHtml();
            DataAnnotationsValidator().ToHtml();
        }

        ctx.Validate();
        Assert.Single(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    // Pushes the scope, renders a DataAnnotationsValidator component (which registers its
    // Inner IFieldValidator into the context), and returns the context for further assertions.
    private static EditContext RegisterValidator(Person p)
    {
        var ctx = new EditContext(p);
        using (EditContextScope.Push(ctx))
        {
            DataAnnotationsValidator().ToHtml();
        }
        return ctx;
    }

    private sealed class Person
    {
        [Required(ErrorMessage = "Name is required")] public string Name { get; set; } = "";
        [Range(1, 120)] public int Age { get; set; }
        [StringLength(5)] public string Code { get; set; } = "";
    }
}
