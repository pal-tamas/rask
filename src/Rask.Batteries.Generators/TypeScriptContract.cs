using System.Collections.Generic;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>One remote message, reduced to what the TypeScript side needs to know about it.</summary>
internal sealed class TypeScriptContract
{
    public string WireName { get; set; } = string.Empty;

    /// <summary>The verb the transport uses: <c>query</c>, <c>command</c> or <c>notification</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public WireType Message { get; set; } = null!;

    /// <summary>Null for a message that answers with nothing.</summary>
    public WireType? Result { get; set; }

    public bool ReturnsFile { get; set; }

    /// <summary>
    ///     The wire names of the message's file-carrying properties, in declaration order.
    /// </summary>
    /// <remarks>
    ///     Order is the contract: the server pairs a multipart part with a property by the index the
    ///     message reserved for it. Getting the order wrong does not fail — it hands the handler
    ///     somebody else's file.
    /// </remarks>
    public IReadOnlyList<string> FileProperties { get; set; } = [];
}
