namespace Rask.Storage.Backends;

/// <summary>
/// The headers stored with an object, so that a CDN in front of a public bucket serves it with the same safe
/// type and disposition the app would.
/// </summary>
internal readonly record struct BlobHeaders(string ContentType, string ContentDisposition, string CacheControl);
