using System.Text.Json;

namespace Rask.Hosting.Shared;

/// <summary>
///     Which <c>npm run</c> script starts a front end's own dev server.
/// </summary>
/// <remarks>
///     Read from the front end's package.json rather than decided per framework, because that file is what
///     actually settles it — create-vite writes <c>dev</c>, the Angular CLI writes <c>start</c>, and a project
///     that renamed either is still answered correctly. <c>rask dev</c> and an app an editor launched both
///     ask here, so they run the same script.
/// </remarks>
internal static class DevScript
{
    /// <summary>The answer when the manifest says nothing usable.</summary>
    internal const string Default = "dev";

    /// <summary>The script to run, given the text of package.json (or null when there is none).</summary>
    internal static string FromManifest(string? packageJson)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            return Default;
        }

        try
        {
            using var document = JsonDocument.Parse(packageJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("scripts", out var scripts)
                && scripts.ValueKind == JsonValueKind.Object)
            {
                if (scripts.TryGetProperty("dev", out _))
                {
                    return "dev";
                }

                if (scripts.TryGetProperty("start", out _))
                {
                    return "start";
                }
            }
        }
        catch (JsonException)
        {
            // Malformed: the common default, rather than refusing to run the host over a file that is only
            // needed for the other half.
        }

        return Default;
    }
}
