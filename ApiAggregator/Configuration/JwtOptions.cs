using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>The token issuer (the "iss" claim).</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The intended audience (the "aud" claim).</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key (HMAC-SHA256). Must be long enough (>= 32 bytes) to be secure.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 60;
}
