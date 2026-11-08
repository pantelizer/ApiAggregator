using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Models;

public sealed class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class TokenResponse
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string TokenType { get; init; } = "Bearer";
}
