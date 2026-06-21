using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApiAggregator.Tests;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "ApiAggregator",
        Audience = "ApiAggregatorClients",
        SigningKey = "this_is_a_test_signing_key_that_is_long_enough_1234567890",
        ExpiryMinutes = 30
    };

    [Fact]
    public void CreateToken_produces_a_token_that_validates_with_the_expected_claims()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), new MutableTimeProvider(start));

        var (token, expiresAt) = service.CreateToken("alice");

        Assert.Equal(start.AddMinutes(30), expiresAt);

        // The token must validate against parameters matching how the API is configured.
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Options.Issuer,
            ValidateAudience = true,
            ValidAudience = Options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.SigningKey)),
            ValidateLifetime = false // we are validating at an arbitrary wall-clock time in the test
        };

        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(token, validationParameters, out var validatedToken);

        Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal("alice", principal.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
    }
}
