using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Pdf.Tests;

namespace Rask.Printing.Tests.Host;

public sealed class PdfFromTests
{
    [Fact]
    public async Task A_page_prints_its_hungarian_text_as_the_server_rendered_it()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await host.As(PrintHost.Anonymous, () => Pdf.From(Routes.RefundReportPage()));

        var text = Assert.Single(PrintedPdf.Pages(pdf)).Text;
        Assert.Contains("Árvíztűrő tükörfúrógép", text, StringComparison.Ordinal);
        Assert.Contains("őű ŐŰ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guarded_page_prints_for_the_signed_in_caller_under_their_own_name()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await host.As(PrintHost.SignedIn("Kovács Ödön"), () => Pdf.From(Routes.MyReportPage()));

        Assert.Contains("Kimutatás neki: Kovács Ödön", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guarded_page_is_never_printed_for_an_anonymous_caller()
    {
        await using var host = await PrintHost.Start();

        var refused = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => host.As(PrintHost.Anonymous, () => Pdf.From(Routes.MyReportPage())));

        Assert.Contains("/print/mine", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_for_admins_is_never_printed_for_a_caller_without_the_role()
    {
        await using var host = await PrintHost.Start();

        var refused = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => host.As(PrintHost.SignedIn("Ödön"), () => Pdf.From(Routes.AdminReportPage())));

        Assert.Contains("not open to this user", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_for_admins_prints_for_an_admin()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await host.As(PrintHost.SignedIn("Ödön", "admin"), () => Pdf.From(Routes.AdminReportPage()));

        Assert.Contains("Csak adminoknak", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_no_page_answers_is_not_printed()
    {
        await using var host = await PrintHost.Start();

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.As(PrintHost.Anonymous, () => Pdf.From(new RouteUrl("/print/nothing-here"))));

        Assert.Contains("No page of this app answers /print/nothing-here", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_address_off_the_app_is_refused_as_not_a_route()
    {
        await using var host = await PrintHost.Start();

        var refused = await Assert.ThrowsAsync<ArgumentException>(
            () => host.As(PrintHost.Anonymous, () => Pdf.From(RouteUrl.External("https://example.com/report"))));

        Assert.Contains("is not one", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_redirects_is_not_printed()
    {
        await using var host = await PrintHost.Start();

        var redirected = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.As(PrintHost.Anonymous, () => Pdf.From(Routes.RedirectingPage())));

        Assert.Contains("sent its reader to", redirected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_component_prints_as_a_whole_document_of_the_app()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        await using var host = await PrintHost.Start();

        var pdf = await host.As(PrintHost.Anonymous, () => Pdf.From(PrintTestApp.ReceiptFor("Szűcs Éva")));

        Assert.Contains("Nyugta: Szűcs Éva", Assert.Single(PrintedPdf.Pages(pdf)).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_prints_in_the_web_font_the_apps_own_stylesheet_names()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        var (font, name) = DocumentServerTests.InstalledMonospace();
        Assert.SkipWhen(font is null, "No monospaced TrueType font is installed here to serve as the web font.");
        await using var host = await PrintHost.Start(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/css/app.css"] = Encoding.UTF8.GetBytes("@font-face { font-family: Riport; src: url(/fonts/riport.ttf); } body { font-family: Riport; }"),
            ["/fonts/riport.ttf"] = await File.ReadAllBytesAsync(font, TestContext.Current.CancellationToken),
        });

        var pdf = await host.As(PrintHost.Anonymous, () => Pdf.From(Routes.RefundReportPage()));

        Assert.Contains(PrintedPdf.Fonts(pdf), used => used.Contains(name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pdf_from_outside_any_work_in_progress_says_there_is_no_one_to_print_for()
    {
        await using var host = await PrintHost.Start();

        var nowhere = await Assert.ThrowsAsync<InvalidOperationException>(() => Pdf.From(Routes.RefundReportPage()));

        Assert.Contains("outside any work in progress", nowhere.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stopping_the_host_closes_its_browser()
    {
        Assert.SkipWhen(TestChromium.Installed is null, "No Chromium is installed here, so nothing printed.");
        var host = await PrintHost.Start();
        await host.As(PrintHost.Anonymous, () => Pdf.From(Routes.RefundReportPage()));
        var browser = host.Services.GetRequiredService<ChromiumPrinter>().Browser!;

        await host.DisposeAsync();

        Assert.True(browser.IsGone);
        Assert.False(Directory.Exists(browser.Profile));
    }

    [Fact]
    public async Task The_settings_bind_from_the_rask_pdf_section()
    {
        await using var host = await PrintHost.Start(chromium: "/opt/chromium/chrome");

        var settings = host.Services.GetRequiredService<ChromiumSettings>();

        Assert.Equal("/opt/chromium/chrome", settings.ChromiumPath);
        Assert.Equal(TestChromium.NoSandbox, settings.NoSandbox);
        Assert.Equal(2, settings.MaxConcurrentPrints);
    }

    [Fact]
    public void Adding_pdf_twice_registers_one_printer()
    {
        var services = new ServiceCollection();

        services.AddRaskPdf().AddRaskPdf();

        Assert.Single(services, service => service.ServiceType == typeof(ChromiumPrinter));
    }
}
