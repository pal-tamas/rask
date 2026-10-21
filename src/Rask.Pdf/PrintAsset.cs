namespace Rask.Printing;

/// <summary>A file the printed page asks for — a stylesheet, a font, an image — as the app serves it.</summary>
internal sealed record PrintAsset(byte[] Body, string ContentType);
