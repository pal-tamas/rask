using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test harness instantiates StubComponent and the built-in outlet directly

namespace Rask.Core.Tests.Messaging;

public partial class ToastFacadeTests : global::Rask.Core.RaskMarkup
{
    private static new Func<Component> Outlet() =>
        () => ToastOutlet.Template((msgs, _) =>
            Div[msgs.Select(m => (Component)Span.Key(m.Id.ToString())[$"{m.Title}: {m.Message} ({m.Action?.Label})"])]);

    [Fact]
    public void The_steps_after_a_toast_are_on_it_when_it_is_drawn()
    {
        var toaster = new Toaster();
        var services = new ServiceCollection().AddSingleton<IToaster>(toaster).BuildServiceProvider();
        var host = new StubComponent(Outlet());
        host.RenderAsLiveRoot(services);
        using var work = Ambient.Enter(services);

        Toast.Info("Product deleted").Heading("Tea").Action("Undo", () => { });
        var html = host.RenderAsLiveRoot(services);

        Assert.Contains("Tea: Product deleted (Undo)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_and_a_link_action_say_where_they_go()
    {
        var toaster = new Toaster();
        var services = new ServiceCollection().AddSingleton<IToaster>(toaster).BuildServiceProvider();
        using var work = Ambient.Enter(services);

        Toast.Success("Invoice created.").Link("View invoice", "/invoices/1").Action("View", "/invoices/1");
        var toast = Assert.Single(toaster.Consume());

        Assert.Equal(new ToastLink("View invoice", "/invoices/1"), toast.Link);
        Assert.Equal("View", toast.Action!.Label);
        Assert.Equal("/invoices/1", toast.Action.Href?.ToString());
    }

    [Fact]
    public void A_fake_records_what_was_shown_and_at_what_level()
    {
        using var toasts = Toast.Fake();

        Toast.Success("Saved").Heading("Order 42");
        Toast.Error("Payment failed").UntilDismissed();

        toasts.Shown("Saved").Once();
        toasts.Shown("Saved").As(ToastLevel.Success);
        toasts.Shown("Deleted").Never();
    }

    [Fact]
    public void A_fake_says_what_was_shown_when_the_toast_it_was_asked_about_was_not()
    {
        using var toasts = Toast.Fake();
        Toast.Success("Saved");

        var error = Assert.Throws<InvalidOperationException>(() => toasts.Shown("Deleted").Once());

        Assert.Contains("the toasts said: \"Saved\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_built_in_outlet_steps_aside_for_one_the_app_mounted()
    {
        var toaster = new Toaster();
        var services = new ServiceCollection().AddSingleton<IToaster>(toaster).BuildServiceProvider();
        var builtIn = new StubComponent(() => new ToastOutlet { Template = (msgs, _) => Div["built-in"], BuiltIn = true });
        var own = new StubComponent(Outlet());
        builtIn.RenderAsLiveRoot(services);
        own.RenderAsLiveRoot(services);

        ((IToaster)toaster).Success("Saved");
        var fromBuiltIn = builtIn.RenderAsLiveRoot(services);
        var fromOwn = own.RenderAsLiveRoot(services);

        Assert.DoesNotContain("built-in", fromBuiltIn, StringComparison.Ordinal);
        Assert.Contains("Saved", fromOwn, StringComparison.Ordinal);
    }

    [Fact]
    public void Rasks_own_look_draws_the_message_its_title_and_its_action()
    {
        var messages = new[]
        {
            new ToastMessage(1, ToastLevel.Error, "Payment failed", "Order 42") { Action = new ToastAction("Retry", default) },
        };

        var html = DefaultToasts.Render(messages, _ => { }, top: false, align: "end").ToHtml();

        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("Order 42", html, StringComparison.Ordinal);
        Assert.Contains("Payment failed", html, StringComparison.Ordinal);
        Assert.Contains(">Retry</button>", html, StringComparison.Ordinal);
    }
}
