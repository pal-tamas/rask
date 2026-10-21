namespace Rask.Printing;

/// <summary>The app's own static files, the only thing a printed page may load besides itself.</summary>
internal interface IPrintAssets
{
    /// <summary>The file served at <paramref name="path"/> (<c>/css/app.css</c>), or null when the app has none there.</summary>
    ValueTask<PrintAsset?> Find(string path, CancellationToken cancellation);
}
