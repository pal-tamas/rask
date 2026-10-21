namespace Rask.Cli.Tests;

/// <summary>
///     Every template image that runs the app on ASP.NET carries Rask.Pdf's browser when the app references it:
///     the publish fetches the pinned shell, and the runtime stage copies it where the locator looks and installs
///     what it links against. An image without the step prints nothing in production, and says so only there.
/// </summary>
public sealed class TemplatePdfBrowserTests
{
    private static readonly string Root = Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    [Fact]
    public void Every_aspnet_template_image_brings_the_pdf_browser_when_the_app_has_one()
    {
        var images = Directory.EnumerateFiles(Root, "Dockerfile", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("mcr.microsoft.com/dotnet/aspnet", StringComparison.Ordinal))
            .ToList();

        var missing = images
            .Where(file => File.ReadAllText(file) is var text
                && !(text.Contains("-p:RaskPdfChromiumCache=/chrome", StringComparison.Ordinal)
                    && text.Contains("COPY --from=build /chrome-shell /opt/rask/chrome", StringComparison.Ordinal)
                    && text.Contains("if [ -f /opt/rask/chrome/chrome-headless-shell ]", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(Root, file))
            .ToList();

        Assert.NotEmpty(images);
        Assert.True(missing.Count == 0, "These images print no PDF in production:\n  " + string.Join("\n  ", missing));
    }
}
