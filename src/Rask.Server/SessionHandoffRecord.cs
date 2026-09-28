namespace Rask.Server;

/// <summary>What a host needs to rebuild a page it has never seen: where it was, and what the app declared.</summary>
/// <param name="Url">The path + query the session was on, in the shape <c>SplitUrl</c> parses.</param>
/// <param name="Entries">The <see cref="Rask.Core.Live.IPersistentState" /> bag, still as UTF-8 JSON.</param>
internal sealed record SessionHandoffRecord(string Url, IReadOnlyList<KeyValuePair<string, byte[]>> Entries);
