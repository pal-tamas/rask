using System.Text.Json.Serialization;

namespace Rask.Example.Auth.WasmCookie;

public sealed record LoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

public sealed record MeDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("roles")] string[] Roles);

// Form-bound model (settable props for two-way binding).
public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

// Source-generated JSON so the WASM publish stays trim-clean (zero IL warnings).
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(MeDto))]
public partial class AuthJson : JsonSerializerContext;
