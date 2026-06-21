namespace ApiAggregator.Infrastructure.Auth;

public interface ITokenService
{

    (string Token, DateTimeOffset ExpiresAt) CreateToken(string username);
}
