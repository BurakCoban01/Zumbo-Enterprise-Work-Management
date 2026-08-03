namespace Zumbo.BuildingBlocks.Application.Security;

public interface ITokenIssuer
{
    string CreateAccessToken(TokenUser user, JwtOptions options, DateTimeOffset now);
    string CreateRefreshToken();
}
