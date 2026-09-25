namespace Rask.Core.Browser;

/// <summary>A supported public-key algorithm (a <c>pubKeyCredParams</c> entry).</summary>
/// <param name="Alg">COSE algorithm id, e.g. <c>-7</c> (ES256) or <c>-257</c> (RS256).</param>
/// <param name="Type">Credential type — always <c>"public-key"</c>.</param>
public sealed record PubKeyCredParam(int Alg, string Type = "public-key");
