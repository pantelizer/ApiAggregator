using ApiAggregator.Infrastructure.Auth;
using ApiAggregator.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiAggregator.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;

    public AuthController(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    /// <summary>Exchange credentials for a JWT.</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> CreateToken([FromBody] LoginRequest request)
    {
        // Demo login: any non-empty credentials.
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized();
        }

        var (token, expiresAt) = _tokenService.CreateToken(request.Username);
        return Ok(new TokenResponse { AccessToken = token, ExpiresAt = expiresAt });
    }
}
