using System.Text.Json.Serialization;
using Rask.Core.Authentication;

namespace Rask.Auth.Client;

/// <summary>
/// Source-generated serializers for the <c>/api/auth</c> shapes.
/// </summary>
/// <remarks>
/// <para>
/// Reflection-based JSON is what a trimmed WebAssembly publish cannot keep: the serializer needs types
/// it discovers at runtime, the trimmer cannot see that, and the result is <c>IL2026</c> at publish and
/// a failure in the browser. A generated context gives the trimmer something static to hold on to, so
/// this half publishes clean.
/// </para>
/// <para>
/// It is the same reason the samples carried a context of their own for these shapes when they
/// hand-wrote this code. Now the framework owns the shapes, it owns their serializers too.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(ForgotPasswordRequest))]
[JsonSerializable(typeof(ResetPasswordRequest))]
[JsonSerializable(typeof(ConfirmEmailRequest))]
[JsonSerializable(typeof(CurrentUser))]
[JsonSerializable(typeof(AuthFailure))]
internal sealed partial class AuthJsonContext : JsonSerializerContext;
