namespace Rask.Core.Printing;

/// <summary>A rendered document and the app path it answers at, without the host's path base.</summary>
internal sealed record PrintablePage(string Html, string Path);
