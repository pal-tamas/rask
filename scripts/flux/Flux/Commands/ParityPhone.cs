using System.Net;
using System.Net.Sockets;

namespace Rask.Flux.Commands;

/// <summary>
///     Holds Ui.Phone's OPEN country list to Flux UI's live documentation. Parity measures the page as loaded, where the
///     list is shut; this opens each example's list — on fluxui.dev and on the page FluxParityPages wrote — and compares
///     what is then on screen (<see cref="OpenList" />): the popup subtree with its 244 rows, where it sits against the
///     button, a row hovered and pressed.
/// </summary>
/// <remarks>
///     The rows are not in the page: the phone engine builds them from intl-tel-input's countries when the list first
///     opens, as Flux's script does. So the Rask page is SERVED, with the engine and its utils from src/Rask.Ui/Resources
///     and the flags from src/Rask.Ui/Flags at the paths an app's build writes them to, and it is given the runtime's phone
///     hook, which fetches the engine as it does in an app.
///     <code>
///     dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
///     flux parity-phone                                             # every example, light and dark
///     flux parity-phone --refresh --all                             # re-measure Flux, every difference
///     </code>
///     Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/phone-open/.
/// </remarks>
[FluxCommand("parity-phone", "a phone field's open country list against Flux's")]
public sealed class ParityPhone : IFluxCommand
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        [".html"] = "text/html",
        [".js"] = "text/javascript",
        [".svg"] = "image/svg+xml",
        [".css"] = "text/css",
    };

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var kit = Repo.Path("src", "Rask.Ui");
        var served = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/js/rask-ui-phone.js"] = Path.Combine(kit, "Resources", "ui-phone.js"),
            ["/js/rask-ui-phone-utils.js"] = Path.Combine(kit, "Resources", "ui-phone-utils.js"),
        };

        // One origin for the page and what it fetches: a file:// page cannot import a module.
        var port = FreePort();
        using var server = new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        using var stop = new CancellationTokenSource();
        var serving = Serve(server, path => served.TryGetValue(path, out var file) ? file
            : path.StartsWith("/flags/", StringComparison.Ordinal) ? Under(Path.Combine(kit, "Flags"), path["/flags/".Length..])
            : Under(Repo.Parity("rask"), path), stop.Token);

        try
        {
            return await OpenList.Run(Kind($"http://127.0.0.1:{port}/phone.html"), args);
        }
        finally
        {
            await stop.CancelAsync();
            server.Stop();
            await serving;
        }
    }

    private static OpenKind Kind(string raskUrl) => new(
        Slug: "phone",
        Name: "phone",
        // A disabled or read-only field opens nothing (measured: Flux's read-only button says aria-readonly and stays shut).
        Root: "[data-flux-phone]:has(button[role=combobox]:not([disabled]):not([aria-readonly])), [data-ui-phone]:has(button[role=combobox]:not([disabled]):not([aria-readonly]))",
        Trigger: "button[role=combobox]:not([disabled]):not([aria-readonly])",
        Popup: "[data-flux-options], [data-ui-options]",
        Rows: "[data-flux-option], [data-ui-option]",
        Native: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui-phone"] = "div",
            ["ui-select"] = "div",
            ["ui-selected"] = "div",
            ["ui-options"] = "div",
            ["ui-option"] = "div",
            ["ui-option-empty"] = "div",
        },
        Says: "(control, trigger) => trigger.getAttribute('aria-label')",
        LiveRoot: "el => el.closest('[data-ui-phone]').parentElement",
        Walks: [])
    {
        RaskUrl = raskUrl,
        Runtime = ["rask-phone.ts"],
        // Every field mounted: the engine marks its picker once it has taken the field over.
        Ready = "[data-ui-phone-ready]",
        AllRows = "[data-flux-option], [data-ui-option]",
        Search = "[data-flux-select-search], [data-ui-select-search]",
    };

    // A path of the request under a folder, or null for one that climbs out of it.
    private static string? Under(string folder, string relative)
    {
        var root = Path.GetFullPath(folder);
        var file = Path.GetFullPath(Path.Combine(root, relative.TrimStart('/', '\\')));
        return file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? file : null;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task Serve(HttpListener server, Func<string, string?> locate, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await server.GetContextAsync().WaitAsync(stop);
            }
            catch (Exception error) when (error is OperationCanceledException or HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            // Each request on its own: the page fetches its flags all at once.
            _ = Answer(context, locate);
        }
    }

    private static async Task Answer(HttpListenerContext context, Func<string, string?> locate)
    {
        using var response = context.Response;
        try
        {
            var file = locate(Uri.UnescapeDataString(context.Request.Url!.AbsolutePath));
            if (file is null || !File.Exists(file))
            {
                response.StatusCode = 404;
                return;
            }

            var body = await File.ReadAllBytesAsync(file);
            response.ContentType = Types.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body);
        }
        catch (HttpListenerException)
        {
            // The page went away mid-answer: nobody is left to answer.
        }
    }
}
