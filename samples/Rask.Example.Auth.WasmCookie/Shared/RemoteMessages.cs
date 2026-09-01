using Rask.Cqrs;

namespace Rask.Example.Auth.WasmCookie;

// The contract both halves compile. It lives in the browser project and is LINKED into the host's
// csproj, which is the two-project arrangement of what `rask new --wasm` does in one: the message is
// one type, and only the handler is server-side. Renaming a property here changes the wire on both
// sides at once, which is the point — a DTO nobody has to keep in step.
//
// Nothing on either record says "remote". Where the handler lives is what decides that: the browser
// compiles no handler for these, and AddRaskCqrsClient() sends anything it has a contract for.

/// <summary>Asks the server who it thinks is calling — so the answer proves the cookie rode the dispatch.</summary>
public sealed record WhoAmI : IQuery<ServerIdentity>;

/// <summary>What the server made of the caller: read from <c>HttpContext.User</c>, not from the message.</summary>
public sealed record ServerIdentity(string Name, string[] Roles);

/// <summary>Mutates server state and answers with the new value — a POST, and never reachable by a url.</summary>
public sealed record NoteVisit : ICommand<int>;
