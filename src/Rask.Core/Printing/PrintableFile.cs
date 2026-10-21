namespace Rask.Core.Printing;

/// <summary>A static file of the app, as it is served.</summary>
internal sealed record PrintableFile(byte[] Body, string ContentType);
