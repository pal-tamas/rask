using Rask.Core.Routing;

namespace Rask.Wasm;

internal static class RouteSeeder
{
    public static void Seed(string browserLocation, RouteState state)
    {
        try
        {
            var location = browserLocation ?? string.Empty;
            var qIndex = location.IndexOf('?');
            var path = qIndex < 0 ? location : location[..qIndex];
            var query = qIndex < 0 ? string.Empty : location[qIndex..];

            // WasmAppHost serves /index.html as the document URL. Treat that as the SPA root.
            if (path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^"/index.html".Length];
            }

            if (path.Length == 0)
            {
                path = "/";
            }

            state.Path = path;
            state.Query = string.IsNullOrEmpty(query) ? QueryCollection.Empty : QueryString.Parse(query);
        }
        catch
        {
            state.Path = "/";
            state.Query = QueryCollection.Empty;
        }
    }
}
