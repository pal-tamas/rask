namespace Rask.Site.Features.UiDocs;

/// <summary>The app frame demo's drivers.</summary>
internal static class AppFrameDemoDrivers
{
    public static IReadOnlyList<AppFrameDemoDriver> All { get; } =
    [
        new("ada", "Ada Lovelace", "C+E", Active: true),
        new("grace", "Grace Hopper", "C", Active: false),
        new("katherine", "Katherine Johnson", "C+E", Active: true),
    ];
}
