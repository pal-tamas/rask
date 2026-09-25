namespace Rask.Api.Generators;

/// <summary>Where a parameter's value travels.</summary>
internal enum ApiBinding
{
    /// <summary>Substituted into the route template.</summary>
    Route,

    /// <summary>Appended to the query string.</summary>
    Query,

    /// <summary>Sent as the JSON request body.</summary>
    Body,

    /// <summary>Sent as a request header.</summary>
    Header,
}
