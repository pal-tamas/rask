namespace Rask.Cli;

/// <summary>Where the front-end dev servers the CLI scaffolds listen by default, named once.</summary>
internal static class LocalDevServers
{
    /// <summary>Vite's own default, and so every template built directly on it.</summary>
    public const string Vite = "http://localhost:5173";

    /// <summary>Nuxt, Next, TanStack Start and SolidStart all default to port 3000.</summary>
    public const string Port3000 = "http://localhost:3000";

    /// <summary>Angular's <c>ng serve</c>.</summary>
    public const string Angular = "http://localhost:4200";
}
