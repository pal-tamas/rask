using Microsoft.AspNetCore.Mvc;

namespace Rask.Tests;

/// <summary>What an app's own API controller looks like: ordinary ASP.NET, nothing Rask-specific.</summary>
/// <remarks>
///     Exists so <see cref="RaskAppTests" /> can ask whether the API battery reaches it without the app
///     writing a line of wiring. MVC discovers controllers by scanning the entry assembly's parts, so a
///     controller declared in the test assembly is found the same way an app's own would be.
/// </remarks>
[ApiController]
[Route("api/probe")]
public sealed class ProbeController : ControllerBase
{
    /// <summary>Answers with the id it was given.</summary>
    /// <param name="value">The id.</param>
    /// <returns>The id, as JSON.</returns>
    [HttpGet("{value:int}")]
    public ActionResult<Probe> Get(int value) => new Probe(value);
}

/// <summary>The probe's answer.</summary>
/// <param name="Value">The id echoed back.</param>
public sealed record Probe(int Value);
