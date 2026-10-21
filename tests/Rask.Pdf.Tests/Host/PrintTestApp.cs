using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a test app that writes its own <head>, as a hand-wired host does

namespace Rask.Printing.Tests.Host;

public sealed partial class PrintTestApp : Component
{
    protected override Component? HeadAssets => Link.Rel("stylesheet").Href("/css/app.css");

    protected override Component? Render() => Router;

    /// <summary>A receipt for one customer, built through its chain as an app's handler builds one.</summary>
    internal static Component ReceiptFor(string customer) => Receipt.Customer(customer);
}

[Route("/print/refund")]
[AllowAnonymous]
public sealed partial class RefundReportPage : Component
{
    protected override Component? Render() => Div[H1["Árvíztűrő tükörfúrógép"], P["őű ŐŰ"]];
}

[Route("/print/mine")]
[Authorize]
public sealed partial class MyReportPage(IUserProvider users) : Component
{
    protected override Component? Render() => P["Kimutatás neki: ", users.Current.Identity?.Name ?? "senki"];
}

[Route("/print/admin")]
[Authorize(Roles = "admin")]
public sealed partial class AdminReportPage : Component
{
    protected override Component? Render() => P["Csak adminoknak"];
}

[Route("/print/elsewhere")]
[AllowAnonymous]
public sealed partial class RedirectingPage : Component
{
    protected override Task OnMount()
    {
        Go.To(Rask.Pdf.Tests.Routes.RefundReportPage());
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["Átirányít"];
}

public sealed partial class Receipt : Component
{
    public string? Customer { get; set; }

    protected override Component? Render() => P["Nyugta: ", Customer ?? string.Empty];
}
