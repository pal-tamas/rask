using System.Text.Json.Serialization;

namespace Rask.Example.Auth.WasmJwt;

public sealed record LoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record TokenResponse([property: JsonPropertyName("token")] string Token);

public sealed record MeDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("roles")] string[] Roles);

public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

// Source-generated JSON so the WASM publish stays trim-clean.
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(MeDto))]
public partial class AuthJson : JsonSerializerContext;
