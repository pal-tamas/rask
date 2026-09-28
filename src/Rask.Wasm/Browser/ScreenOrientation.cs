using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IScreenOrientation" />, backed by the unified <see cref="IJSRuntime" />. Reads go
///     through the framework's <c>__raskOrientation</c> helper, which returns the orientation as a plain
///     <c>{ type, angle }</c> object; the hyphenated type string is mapped to <see cref="OrientationType" />
///     in C#.
/// </summary>
public sealed class ScreenOrientation(IJSRuntime js) : IScreenOrientation
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskOrientation.isSupported");

    /// <inheritdoc />
    public async ValueTask<OrientationInfo> GetAsync()
    {
        var reading = await js.InvokeAsync<OrientationReading>("__raskOrientation.get").ConfigureAwait(false);
        return new OrientationInfo(MapType(reading.Type), reading.Angle);
    }

    /// <inheritdoc />
    public ValueTask LockAsync(OrientationLock orientation) =>
        js.InvokeVoidAsync("__raskOrientation.lock", ToSpecName(orientation));

    /// <inheritdoc />
    public ValueTask UnlockAsync() => js.InvokeVoidAsync("__raskOrientation.unlock");

    private static OrientationType MapType(string? type) => type switch
    {
        "portrait-primary" => OrientationType.PortraitPrimary,
        "portrait-secondary" => OrientationType.PortraitSecondary,
        "landscape-primary" => OrientationType.LandscapePrimary,
        "landscape-secondary" => OrientationType.LandscapeSecondary,
        _ => OrientationType.Unknown
    };

    // The Screen Orientation API uses hyphenated lowercase lock names.
    private static string ToSpecName(OrientationLock orientation) => orientation switch
    {
        OrientationLock.Any => "any",
        OrientationLock.Natural => "natural",
        OrientationLock.Portrait => "portrait",
        OrientationLock.Landscape => "landscape",
        OrientationLock.PortraitPrimary => "portrait-primary",
        OrientationLock.PortraitSecondary => "portrait-secondary",
        OrientationLock.LandscapePrimary => "landscape-primary",
        OrientationLock.LandscapeSecondary => "landscape-secondary",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown orientation lock.")
    };
}
