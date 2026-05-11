using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Authentication;

public sealed class LoginModel
{
    [Required] public string? Username { get; set; }
    [Required] public string? Password { get; set; }
}
