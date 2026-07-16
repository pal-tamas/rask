using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// End-to-end for the Rask.Mail slice against the EF Core + SQLite sample: fill the compose form, submit,
// and assert both the immediate on-page confirmation (the queue write) and that the background
// MailProcessor actually delivered the message — here as an .eml file in the pickup directory.
[Collection(EfCoreExampleCollection.Name)]
public sealed class EfCoreMailTests(EfCoreExampleAppFixture app, PlaywrightFixture pw) : IAsyncLifetime
{
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public async Task InitializeAsync()
    {
        _ctx = await pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = app.BaseUrl });
        _page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task Sending_email_queues_it_and_the_processor_delivers_it()
    {
        await _page.GotoAsync("/mail");

        await _page.FillAsync("#mail-to", "jane@example.com");
        await _page.FillAsync("#mail-subject", "E2E hello");
        await _page.FillAsync("#mail-body", "Sent from an E2E test.");
        await _page.ClickAsync("#mail-send");

        // Immediate confirmation — the send returned as soon as the row was written.
        await Assertions.Expect(_page.Locator("#mail-sent")).ToBeVisibleAsync();

        // The background processor (1s poll) delivers to the pickup directory; wait for a readable .eml.
        var contents = await WaitForEmlContentAsync(app.MailPickupDirectory, TimeSpan.FromSeconds(15));
        Assert.Contains("Subject: E2E hello", contents, StringComparison.Ordinal);
        Assert.Contains("jane@example.com", contents, StringComparison.Ordinal);
    }

    // Polls for an .eml and reads it once it is fully written. The pickup sender creates the file with
    // FileShare.None while it streams the body, so a read mid-write throws IOException — tolerate that and
    // retry until the write completes (or the timeout elapses).
    private static async Task<string> WaitForEmlContentAsync(string directory, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var eml = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.eml").FirstOrDefault()
                : null;

            if (eml is not null)
            {
                try
                {
                    return await File.ReadAllTextAsync(eml);
                }
                catch (IOException)
                {
                    // The sender still holds the exclusive write handle — try again.
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"No readable .eml file appeared in '{directory}' within {timeout.TotalSeconds}s.");
    }
}
