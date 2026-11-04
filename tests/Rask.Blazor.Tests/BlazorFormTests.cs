using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     A hosted form submits (#1195): Blazor's <c>@onsubmit</c> rides Rask's form channel, so an <c>EditForm</c> inside
///     an island validates and runs its submit callbacks with what its inputs were bound to.
/// </summary>
public partial class BlazorFormTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void A_hosted_forms_submit_is_wired_to_Rasks_form_channel()
    {
        var page = Page.Render(SignUpIsland, Services());

        var html = page.Render();

        Assert.Contains("data-rask-on-submit=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_EditForm_submits_what_its_InputText_and_InputCheckbox_were_bound_to()
    {
        var page = Page.Render(SignUpIsland, Services());

        await page.On("#name").Change("Ada");
        await page.On("#agreed").Change("true");
        await page.On("form").Submit("""{"type":"submit","form":{"Name":"Ada","Agreed":"true"}}""");

        Assert.Contains("outcome: valid Ada True", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_EditForm_whose_rules_fail_runs_its_invalid_submit_instead()
    {
        var page = Page.Render(SignUpIsland, Services());

        await page.On("#agreed").Change("true");
        await page.On("form").Submit("""{"type":"submit","form":{"Agreed":"true"}}""");

        Assert.Contains("outcome: invalid", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_EditForm_with_its_own_OnSubmit_runs_it_whatever_the_rules_say()
    {
        var page = Page.Render(PlainSubmitIsland, Services());

        await page.On("#name").Change("Grace");
        await page.On("form").Submit("""{"type":"submit","form":{"Name":"Grace"}}""");

        Assert.Contains("outcome: submitted Grace", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hosted_submit_handler_takes_the_form_channels_shape()
    {
        var submit = new BlazorHandler(1, "submit", typeof(EventArgs), BlazorValueKind.Text);

        var bound = submit.Bind(_ => Task.CompletedTask);

        Assert.True(submit.CanBind);
        Assert.IsType<Func<FormData, Task>>(bound);
    }

    [Fact]
    public async Task An_oninput_bind_on_a_checkbox_rides_change_and_receives_a_bool()
    {
        var page = Page.Render(OnInputIsland, Services());

        await page.On("#on").Change("true");

        Assert.Null(page.Find("#on").Attribute("data-rask-on-input"));
        Assert.Contains("on: True", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oninput_bind_on_a_multi_select_rides_change_and_receives_every_selected_value()
    {
        var page = Page.Render(OnInputIsland, Services());

        await page.On("#tags").Raise("change", """{"type":"change","value":"red","values":["red","blue"]}""");

        Assert.Contains("picked: red,blue", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_oninput_bind_beside_the_controls_own_onchange_stays_on_input()
    {
        var page = Page.Render(OnInputIsland, Services());

        page.Render();
        var both = page.Find("#both");

        Assert.NotNull(both.Attribute("data-rask-on-input"));
        Assert.NotNull(both.Attribute("data-rask-on-change"));
    }
}

/// <summary>The model the hosted forms edit.</summary>
public sealed class SignUp
{
    [Required]
    public string? Name { get; set; }

    public bool Agreed { get; set; }
}

/// <summary>What a <c>.razor</c> <c>&lt;EditForm&gt;</c> with an <c>InputText</c> and an <c>InputCheckbox</c> compiles to.</summary>
public sealed class SignUpBox : ComponentBase
{
    private readonly SignUp _model = new();
    private string _outcome = "none";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<EditForm>(0);
        builder.AddComponentParameter(1, nameof(EditForm.Model), _model);
        builder.AddComponentParameter(2, nameof(EditForm.OnValidSubmit), EventCallback.Factory.Create<EditContext>(
            this, () => _outcome = $"valid {_model.Name} {_model.Agreed}"));
        builder.AddComponentParameter(3, nameof(EditForm.OnInvalidSubmit), EventCallback.Factory.Create<EditContext>(
            this, () => _outcome = "invalid"));
        builder.AddComponentParameter(4, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ => Fields));
        builder.CloseComponent();

        builder.OpenElement(5, "p");
        builder.AddContent(6, $"outcome: {_outcome}");
        builder.CloseElement();
    }

    private void Fields(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DataAnnotationsValidator>(0);
        builder.CloseComponent();
        builder.OpenRegion(1);
        FormFields.Name(builder, this, _model);
        builder.CloseRegion();
        builder.OpenRegion(2);
        FormFields.Agreed(builder, this, _model);
        builder.CloseRegion();
    }
}

/// <summary>An <c>EditForm</c> that takes its submit itself, through <c>OnSubmit</c>.</summary>
public sealed class PlainSubmitBox : ComponentBase
{
    private readonly SignUp _model = new();
    private string _outcome = "none";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<EditForm>(0);
        builder.AddComponentParameter(1, nameof(EditForm.Model), _model);
        builder.AddComponentParameter(2, nameof(EditForm.OnSubmit), EventCallback.Factory.Create<EditContext>(
            this, () => _outcome = $"submitted {_model.Name}"));
        builder.AddComponentParameter(3, nameof(EditForm.ChildContent),
            (RenderFragment<EditContext>)(_ => b => FormFields.Name(b, this, _model)));
        builder.CloseComponent();

        builder.OpenElement(4, "p");
        builder.AddContent(5, $"outcome: {_outcome}");
        builder.CloseElement();
    }
}

/// <summary>
///     The two inputs as <c>@bind-Value</c> writes them: the value, its setter and the expression naming it. Each is
///     its own region at the call site, so their literal sequence numbers never meet.
/// </summary>
internal static class FormFields
{
    public static void Name(RenderTreeBuilder builder, object receiver, SignUp model)
    {
        builder.OpenComponent<InputText>(0);
        builder.AddComponentParameter(1, "id", "name");
        builder.AddComponentParameter(2, nameof(InputText.Value), model.Name);
        builder.AddComponentParameter(3, nameof(InputText.ValueChanged),
            EventCallback.Factory.Create<string?>(receiver, v => model.Name = v));
        builder.AddComponentParameter(4, nameof(InputText.ValueExpression),
            (Expression<Func<string?>>)(() => model.Name));
        builder.CloseComponent();
    }

    public static void Agreed(RenderTreeBuilder builder, object receiver, SignUp model)
    {
        builder.OpenComponent<InputCheckbox>(0);
        builder.AddComponentParameter(1, "id", "agreed");
        builder.AddComponentParameter(2, nameof(InputCheckbox.Value), model.Agreed);
        builder.AddComponentParameter(3, nameof(InputCheckbox.ValueChanged),
            EventCallback.Factory.Create<bool>(receiver, v => model.Agreed = v));
        builder.AddComponentParameter(4, nameof(InputCheckbox.ValueExpression),
            (Expression<Func<bool>>)(() => model.Agreed));
        builder.CloseComponent();
    }
}

/// <summary>What <c>@bind:event="oninput"</c> compiles to on a checkbox and a <c>&lt;select multiple&gt;</c>.</summary>
public sealed class OnInputBox : ComponentBase
{
    private bool _on;
    private string[] _picked = [];
    private string _seen = "nothing";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "id", "on");
        builder.AddAttribute(2, "type", "checkbox");
        builder.AddAttribute(3, "checked", _on);
        builder.AddAttribute(4, "oninput", EventCallback.Factory.CreateBinder(this, v => _on = v, _on));
        builder.CloseElement();

        builder.OpenElement(5, "select");
        builder.AddAttribute(6, "id", "tags");
        builder.AddAttribute(7, "multiple", true);
        builder.AddAttribute(8, "oninput", EventCallback.Factory.CreateBinder<string[]>(this, v => _picked = v, _picked));
        builder.CloseElement();

        // A checkbox with its own @onchange beside the oninput binder: the change attribute is that handler's.
        builder.OpenElement(9, "input");
        builder.AddAttribute(10, "id", "both");
        builder.AddAttribute(11, "type", "checkbox");
        builder.AddAttribute(12, "oninput", EventCallback.Factory.CreateBinder(this, v => _on = v, _on));
        builder.AddAttribute(13, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _seen = $"{e.Value}"));
        builder.CloseElement();

        builder.OpenElement(14, "p");
        builder.AddContent(15, $"on: {_on} picked: {string.Join(',', _picked)} seen: {_seen}");
        builder.CloseElement();
    }
}

public sealed partial class SignUpIsland : BlazorComponent<SignUpBox>;

public sealed partial class PlainSubmitIsland : BlazorComponent<PlainSubmitBox>;

public sealed partial class OnInputIsland : BlazorComponent<OnInputBox>;
