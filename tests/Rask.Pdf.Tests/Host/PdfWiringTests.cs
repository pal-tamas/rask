using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server;

namespace Rask.Printing.Tests.Host;

public sealed class PdfWiringTests
{
    [Fact]
    public async Task A_minimal_api_endpoint_prints_with_its_requests_services()
    {
        await using var host = await PrintHost.Start();

        using var response = await host.Http.GetAsync(new Uri("/download/refund", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pdf = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Árvíztűrő tükörfúrógép", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_minimal_api_endpoint_prints_a_guarded_page_for_the_requests_own_user()
    {
        await using var host = await PrintHost.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/download/mine");
        request.Headers.Add(PrintHost.UserHeader, "Tóth Ilona");

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pdf = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Kimutatás neki: Tóth Ilona", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_minimal_api_endpoint_never_prints_a_guarded_page_for_an_anonymous_request()
    {
        await using var host = await PrintHost.Start();

        using var response = await host.Http.GetAsync(new Uri("/download/mine", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_rask_app_that_references_rask_pdf_has_the_printer_without_asking()
    {
        var app = RaskApp.Create([], builder =>
        {
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            builder.Configuration["Rask:Pdf:ChromiumPath"] = "/opt/chromium/chrome";
        }).Build<PrintTestApp>();
        await using var _ = app;

        var printer = app.Services.GetService<ChromiumPrinter>();

        Assert.NotNull(printer);
        Assert.Equal("/opt/chromium/chrome", app.Services.GetRequiredService<ChromiumSettings>().ChromiumPath);
    }

    [Fact]
    public async Task A_hand_wired_host_that_references_rask_pdf_has_the_printer_without_asking()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Rask:Pdf:ChromiumPath"] = "/opt/chromium/chrome";

        builder.Services.AddRask();
        await using var app = builder.Build();

        Assert.NotNull(app.Services.GetService<ChromiumPrinter>());
        Assert.Equal("/opt/chromium/chrome", app.Services.GetRequiredService<ChromiumSettings>().ChromiumPath);
    }

    [Fact]
    public void A_hand_wired_host_that_also_adds_pdf_itself_registers_one_printer()
    {
        var services = new ServiceCollection();

        services.AddRask();
        services.AddRaskPdf();

        Assert.Single(services, service => service.ServiceType == typeof(ChromiumPrinter));
    }
}
