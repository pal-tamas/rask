namespace Rask.Web;

/// <summary>One call or write the code under test made on a faked web object.</summary>
/// <param name="Member">The member's MDN name, from the faked object: <c>writeText</c>, <c>clipboard.writeText</c>, <c>title=</c> for a write.</param>
/// <param name="Args">What it was called with.</param>
public sealed record WebCall(string Member, IReadOnlyList<object?> Args);
